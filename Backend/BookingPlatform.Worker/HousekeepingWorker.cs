using BookingPlatform.Application.Common.Time;
using BookingPlatform.Infrastructure.Housekeeping;
using Microsoft.Extensions.Options;

namespace BookingPlatform.Worker;

public sealed class HousekeepingOptions
{
    public const string SectionName = "Housekeeping";

    public int HoldSweepSeconds { get; set; } = 30;

    public int PurgeMinutes { get; set; } = 60;

    public int BatchSize { get; set; } = 500;

    public bool IsValid() => HoldSweepSeconds is >= 5 and <= 3600 && PurgeMinutes is >= 1 and <= 1440 && BatchSize is >= 10 and <= 10000;
}

/// <summary>
/// Expired-hold sweeper and retention purges. Each run uses a fresh scope (own connection); failures are logged and
/// retried on the next tick, never crashing the host.
/// </summary>
public sealed class HousekeepingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<HousekeepingOptions> options,
    IClock clock,
    ILogger<HousekeepingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var sweepInterval = TimeSpan.FromSeconds(settings.HoldSweepSeconds);
        var purgeInterval = TimeSpan.FromMinutes(settings.PurgeMinutes);
        var nextPurge = DateTime.MinValue;

        using var timer = new PeriodicTimer(sweepInterval);
        do
        {
            await RunSafelyAsync("hold sweep", async repository =>
            {
                int expired;
                var total = 0;
                do
                {
                    expired = await repository.SweepExpiredHoldsAsync(clock.UtcNow, settings.BatchSize, stoppingToken);
                    total += expired;
                }
                while (expired >= settings.BatchSize && !stoppingToken.IsCancellationRequested);

                if (total > 0)
                {
                    logger.LogInformation("Expired {HoldCount} holds", total);
                }
            }, stoppingToken);

            if (clock.UtcNow >= nextPurge)
            {
                nextPurge = clock.UtcNow + purgeInterval;
                await RunSafelyAsync("retention purge", async repository =>
                {
                    var result = await repository.PurgeAsync(clock.UtcNow, settings.BatchSize, stoppingToken);
                    if (result.Total > 0)
                    {
                        logger.LogInformation(
                            "Purged {IdempotencyKeys} idempotency keys, {RefreshTokens} refresh tokens, {OutboxMessages} outbox messages, "
                            + "{PasswordResetRequests} password reset requests, {NotificationDeliveries} notification deliveries",
                            result.IdempotencyKeys, result.RefreshTokens, result.OutboxMessages, result.PasswordResetRequests,
                            result.NotificationDeliveries);
                    }
                }, stoppingToken);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private async Task RunSafelyAsync(string job, Func<HousekeepingRepository, Task> action, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<HousekeepingRepository>());
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Housekeeping job {Job} failed; it will be retried", job);
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

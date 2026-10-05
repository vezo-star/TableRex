using BookingPlatform.Application.Common.Time;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BookingPlatform.Infrastructure.Outbox;

public sealed record OutboxDispatcherSettings(
    string Destination,
    int BatchSize,
    TimeSpan PollInterval,
    TimeSpan Lease,
    int MaxAttempts,
    TimeSpan? MaxAge = null);

/// <summary>Thrown by handlers for messages that can never succeed (malformed payload); the row is dead-lettered at once.</summary>
public sealed class OutboxPermanentException(string message) : Exception(message);

public static class OutboxRetryPolicy
{
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(15);

    /// <summary>Exponential backoff (2, 4, 8 … seconds after attempts 1, 2, 3 …) with ±20 % jitter, capped at 15 minutes.</summary>
    public static TimeSpan Delay(int attempt, double jitter)
    {
        var seconds = Math.Pow(2, Math.Clamp(attempt, 1, 20)) * (1 + Math.Clamp(jitter, -0.2, 0.2));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxDelay.TotalSeconds));
    }
}

/// <summary>
/// Drains one outbox destination: woken by <c>LISTEN outbox</c> (written by <c>pg_notify</c> on commit) with a polling
/// fallback, claims batches with SKIP LOCKED, retries with backoff and dead-letters after the attempt limit. A database
/// outage is logged and retried; it never stops the host.
/// </summary>
public abstract class OutboxDispatcher(OutboxStore store, NpgsqlDataSource dataSource, IClock clock, ILogger logger) : BackgroundService
{
    private const string Channel = "outbox";
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    private readonly string _instance = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected abstract OutboxDispatcherSettings Settings { get; }

    protected abstract Task HandleAsync(ClaimedOutboxMessage message, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var listener = await dataSource.OpenConnectionAsync(stoppingToken);
                await using (var listen = new NpgsqlCommand($"LISTEN {Channel}", listener))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                failures = 0;
                while (!stoppingToken.IsCancellationRequested)
                {
                    await DrainAsync(stoppingToken);
                    await listener.WaitAsync(Settings.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failures++;
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, failures), MaxReconnectDelay.TotalSeconds));
                logger.LogWarning(ex, "Outbox dispatcher for {Destination} failed; retrying in {DelaySeconds} s", Settings.Destination, delay.TotalSeconds);
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Claims and dispatches batches until fewer than a full batch is pending.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        var settings = Settings;
        IReadOnlyList<ClaimedOutboxMessage> batch;
        do
        {
            batch = await store.ClaimAsync(settings.Destination, settings.BatchSize, settings.Lease, _instance, cancellationToken);
            foreach (var message in batch)
            {
                await DispatchAsync(message, settings, cancellationToken);
            }
        }
        while (batch.Count >= settings.BatchSize && !cancellationToken.IsCancellationRequested);
    }

    private async Task DispatchAsync(ClaimedOutboxMessage message, OutboxDispatcherSettings settings, CancellationToken cancellationToken)
    {
        if (settings.MaxAge is { } maxAge && clock.UtcNow - message.CreatedAt > maxAge)
        {
            await store.MarkProcessedAsync(message.Id, $"Skipped: older than {maxAge.TotalSeconds:0} s when dispatched.", cancellationToken);
            return;
        }

        try
        {
            await HandleAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The lease expires and another dispatcher (or this one after restart) reclaims the row.
            throw;
        }
        catch (Exception ex)
        {
            var permanent = ex is OutboxPermanentException or System.Text.Json.JsonException;
            var dead = permanent || message.AttemptCount >= settings.MaxAttempts;
            var delay = OutboxRetryPolicy.Delay(message.AttemptCount, (Random.Shared.NextDouble() * 0.4) - 0.2);
            logger.LogWarning(ex, "Outbox message {MessageId} ({MessageType}) failed on attempt {Attempt}; {Outcome}",
                message.Id, message.Type, message.AttemptCount, dead ? "dead-lettered" : $"retrying in {delay.TotalSeconds:0} s");
            await store.MarkFailedAsync(message.Id, $"{ex.GetType().Name}: {ex.Message}", delay, dead, cancellationToken);
            return;
        }

        await store.MarkProcessedAsync(message.Id, null, cancellationToken);
    }
}

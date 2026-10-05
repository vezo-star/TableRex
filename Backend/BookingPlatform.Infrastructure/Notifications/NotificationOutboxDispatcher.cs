using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Application.Notifications;
using BookingPlatform.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BookingPlatform.Infrastructure.Notifications;

/// <summary>
/// Drains NOTIFICATION outbox rows in the Worker. Unlike realtime events, emails never go stale by age alone: the
/// service re-reads the booking and skips what no longer applies. Transport failures retry with backoff and are
/// dead-lettered after <see cref="NotificationOptions.MaxAttempts"/>.
/// </summary>
public sealed class NotificationOutboxDispatcher(
    OutboxStore store,
    NpgsqlDataSource dataSource,
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationOptions> options,
    IClock clock,
    ILogger<NotificationOutboxDispatcher> logger) : OutboxDispatcher(store, dataSource, clock, logger)
{
    protected override OutboxDispatcherSettings Settings { get; } = new(
        OutboxDestinations.Notification,
        options.Value.BatchSize,
        TimeSpan.FromSeconds(options.Value.PollSeconds),
        Lease: TimeSpan.FromMinutes(2),
        MaxAttempts: options.Value.MaxAttempts);

    protected override async Task HandleAsync(ClaimedOutboxMessage message, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<NotificationService>();
        try
        {
            await service.HandleAsync(message.Id, message.TenantId, message.Type, message.Payload, cancellationToken);
        }
        catch (PermanentNotificationException ex)
        {
            throw new OutboxPermanentException(ex.Message);
        }
    }
}

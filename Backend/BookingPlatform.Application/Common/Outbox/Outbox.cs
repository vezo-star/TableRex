namespace BookingPlatform.Application.Common.Outbox;

public static class OutboxDestinations
{
    public const string Realtime = "REALTIME";
    public const string Notification = "NOTIFICATION";
}

/// <summary>SignalR client method names (REALTIME.md §3). Never rename a value; clients depend on them.</summary>
public static class RealtimeEvents
{
    public const string BookingCreated = "BookingCreated";
    public const string BookingUpdated = "BookingUpdated";
    public const string BookingCancelled = "BookingCancelled";
    public const string BookingMoved = "BookingMoved";
    public const string BookingStatusChanged = "BookingStatusChanged";
    public const string ResourceUpdated = "ResourceUpdated";
    public const string ResourceBlocked = "ResourceBlocked";
    public const string HoldCreated = "HoldCreated";
    public const string HoldExpired = "HoldExpired";
    public const string FloorPlanUpdated = "FloorPlanUpdated";
}

public static class NotificationTypes
{
    public const string BookingConfirmation = "BookingConfirmation";
    public const string BookingCancellation = "BookingCancellation";
}

/// <summary>Minimal realtime payload: no PII, clients refetch through the authorised API (REALTIME.md §3).</summary>
public sealed record RealtimePayload(
    Guid EventId,
    string Type,
    Guid VenueId,
    Guid EntityId,
    int? Version,
    DateOnly? BusinessDate,
    DateTime OccurredAt);

/// <summary>Notification payload: ids only; the notification worker loads contact details when it sends.</summary>
public sealed record NotificationPayload(Guid EventId, string Type, Guid VenueId, Guid BookingId, DateTime OccurredAt);

public sealed record OutboxMessage(Guid Id, Guid? TenantId, string Destination, string Type, object Payload);

public interface IOutboxWriter
{
    /// <summary>Inserts the messages in the current transaction and signals <c>pg_notify('outbox', destination)</c> on commit.</summary>
    Task WriteAsync(IReadOnlyCollection<OutboxMessage> messages, CancellationToken cancellationToken);
}

public static class OutboxWriterExtensions
{
    public static Task WriteRealtimeAsync(
        this IOutboxWriter writer, Guid tenantId, string type, Guid venueId, Guid entityId, int? version, DateOnly? businessDate,
        DateTime occurredAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var id = Guid.CreateVersion7();
        return writer.WriteAsync(
            [new OutboxMessage(id, tenantId, OutboxDestinations.Realtime, type,
                new RealtimePayload(id, type, venueId, entityId, version, businessDate, occurredAt))],
            cancellationToken);
    }

    public static Task WriteNotificationAsync(
        this IOutboxWriter writer, Guid tenantId, string type, Guid venueId, Guid bookingId, DateTime occurredAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var id = Guid.CreateVersion7();
        return writer.WriteAsync(
            [new OutboxMessage(id, tenantId, OutboxDestinations.Notification, type, new NotificationPayload(id, type, venueId, bookingId, occurredAt))],
            cancellationToken);
    }
}

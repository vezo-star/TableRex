using BookingPlatform.Application.Notifications;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Notifications;

public sealed class NotificationRepository(DbSession db) : INotificationRepository
{
    public const int MaxDetailLength = 500;

    public async Task<bool> IsSettledAsync(Guid outboxMessageId, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM notification_deliveries WHERE outbox_message_id = @outboxMessageId AND channel = 'EMAIL')",
            new { outboxMessageId },
            cancellationToken);

    public async Task<bool> WasSentAsync(Guid bookingId, string template, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1 FROM notification_deliveries
                WHERE booking_id = @bookingId AND template = @template AND status = 'SENT')
            """,
            new { bookingId, template },
            cancellationToken);

    public Task<BookingNotificationDetails?> GetBookingAsync(Guid tenantId, Guid bookingId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<BookingNotificationDetails>(
            """
            SELECT b.id AS booking_id, b.tenant_id, t.name AS tenant_name, v.name AS venue_name, v.phone AS venue_phone,
                   v.email AS venue_email, v.address_line1 AS venue_address_line1, v.city AS venue_city, v.time_zone,
                   b.reference, b.status, b.starts_at, b.ends_at, b.party_size, b.special_requests,
                   c.first_name AS customer_first_name, c.email::text AS customer_email, c.anonymized_at AS customer_anonymized_at,
                   (b.public_token_hash IS NOT NULL) AS has_manage_link
            FROM bookings b
            JOIN venues v ON v.id = b.venue_id AND v.tenant_id = b.tenant_id
            JOIN tenants t ON t.id = b.tenant_id
            LEFT JOIN customers c ON c.id = b.customer_id AND c.tenant_id = b.tenant_id
            WHERE b.tenant_id = @tenantId AND b.id = @bookingId
            """,
            new { tenantId, bookingId },
            cancellationToken);

    public Task RecordAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        return db.ExecuteAsync(
            """
            INSERT INTO notification_deliveries
                (outbox_message_id, tenant_id, channel, type, template, booking_id, user_id, status, provider, provider_message_id, detail)
            VALUES
                (@OutboxMessageId, @TenantId, 'EMAIL', @Type, @Template, @BookingId, @UserId, @Status, @Provider, @ProviderMessageId, @Detail)
            ON CONFLICT (outbox_message_id, channel) DO NOTHING
            """,
            new
            {
                delivery.OutboxMessageId,
                delivery.TenantId,
                delivery.Type,
                delivery.Template,
                delivery.BookingId,
                delivery.UserId,
                delivery.Status,
                delivery.Provider,
                ProviderMessageId = Truncate(delivery.ProviderMessageId),
                Detail = Truncate(delivery.Detail),
            },
            cancellationToken);
    }

    private static string? Truncate(string? value) =>
        value is null || value.Length <= MaxDetailLength ? value : value[..MaxDetailLength];
}

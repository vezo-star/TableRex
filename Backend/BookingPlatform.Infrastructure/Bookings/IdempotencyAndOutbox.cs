using System.Text.Json;
using BookingPlatform.Application.Common.Idempotency;
using BookingPlatform.Application.Common.Outbox;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Bookings;

public sealed class IdempotencyStore(DbSession db) : IIdempotencyStore
{
    public async Task<bool> TryBeginAsync(Guid tenantId, string scope, string key, byte[] requestHash, DateTime expiresAt, CancellationToken cancellationToken)
    {
        // An expired key that the Worker has not purged yet must not block reuse.
        await db.ExecuteAsync(
            "DELETE FROM idempotency_keys WHERE tenant_id = @tenantId AND scope = @scope AND key = @key AND expires_at <= now()",
            new { tenantId, scope, key }, cancellationToken);
        var id = await db.ExecuteScalarAsync<Guid?>(
            """
            INSERT INTO idempotency_keys (tenant_id, scope, key, request_hash, expires_at)
            VALUES (@tenantId, @scope, @key, @requestHash, @expiresAt)
            ON CONFLICT (tenant_id, scope, key) DO NOTHING
            RETURNING id
            """,
            new { tenantId, scope, key, requestHash, expiresAt }, cancellationToken);
        return id is not null;
    }

    public Task<IdempotencyRecord?> GetAsync(Guid tenantId, string scope, string key, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<IdempotencyRecord>(
            """
            SELECT request_hash, status, response_status, response_body::text AS response_body, resource_id
            FROM idempotency_keys WHERE tenant_id = @tenantId AND scope = @scope AND key = @key
            """,
            new { tenantId, scope, key }, cancellationToken);

    public Task CompleteAsync(Guid tenantId, string scope, string key, int responseStatus, string responseBody, Guid? resourceId, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE idempotency_keys
            SET status = 'COMPLETED', response_status = @responseStatus, response_body = @responseBody::json, resource_id = @resourceId
            WHERE tenant_id = @tenantId AND scope = @scope AND key = @key
            """,
            new { tenantId, scope, key, responseStatus, responseBody, resourceId }, cancellationToken);
}

/// <summary>Writes outbox rows through the scoped session (same transaction as the business change).</summary>
public sealed class OutboxWriter(DbSession db) : IOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(IReadOnlyCollection<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            await db.ExecuteAsync(
                """
                INSERT INTO outbox_messages (id, tenant_id, destination, type, payload)
                VALUES (@Id, @TenantId, @Destination, @Type, @Payload::jsonb)
                """,
                new
                {
                    message.Id,
                    message.TenantId,
                    message.Destination,
                    message.Type,
                    Payload = JsonSerializer.Serialize(message.Payload, message.Payload.GetType(), JsonOptions),
                },
                cancellationToken);
        }

        foreach (var destination in messages.Select(m => m.Destination).Distinct())
        {
            await db.ExecuteAsync("SELECT pg_notify('outbox', @destination)", new { destination }, cancellationToken);
        }
    }
}

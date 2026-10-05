using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Housekeeping;

public sealed record HousekeepingPurgeResult(int IdempotencyKeys, int RefreshTokens, int OutboxMessages);

/// <summary>
/// Worker maintenance. Correctness never depends on these running: availability ignores expired holds at read time
/// and booking transactions release them lazily (ARCHITECTURE.md, DATABASE.md §4.3).
/// </summary>
public sealed class HousekeepingRepository(DbSession db)
{
    public static readonly TimeSpan RefreshTokenRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan ProcessedOutboxRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// Deactivates a batch of expired hold allocations, marks their holds EXPIRED and writes one <c>HoldExpired</c>
    /// realtime outbox row per hold, in one transaction. Returns the number of holds expired.
    /// </summary>
    public async Task<int> SweepExpiredHoldsAsync(DateTime now, int batchSize, CancellationToken cancellationToken)
    {
        await db.BeginAsync(cancellationToken);
        try
        {
            var expired = await db.ExecuteScalarAsync<int>(
                """
                WITH released AS (
                    UPDATE booking_allocations
                    SET is_active = false, released_at = @now, release_reason = 'HOLD_EXPIRED'
                    WHERE id IN (
                        SELECT id FROM booking_allocations
                        WHERE allocation_type = 'HOLD' AND is_active AND expires_at <= @now
                        ORDER BY expires_at
                        LIMIT @batchSize
                        FOR UPDATE SKIP LOCKED)
                    RETURNING hold_id),
                expired AS (
                    UPDATE resource_holds
                    SET status = 'EXPIRED', released_at = @now, updated_at = @now
                    WHERE status = 'ACTIVE' AND (id IN (SELECT hold_id FROM released) OR expires_at <= @now)
                    RETURNING id, tenant_id, venue_id),
                messages AS (
                    SELECT uuidv7() AS message_id, id, tenant_id, venue_id FROM expired),
                written AS (
                    INSERT INTO outbox_messages (id, tenant_id, destination, type, payload)
                    SELECT message_id, tenant_id, 'REALTIME', 'HoldExpired',
                           jsonb_build_object('eventId', message_id, 'type', 'HoldExpired', 'venueId', venue_id, 'entityId', id,
                                              'version', NULL, 'businessDate', NULL, 'occurredAt', @now)
                    FROM messages
                    RETURNING 1)
                SELECT count(*)::int FROM written
                """,
                new { now, batchSize },
                cancellationToken);
            if (expired > 0)
            {
                await db.ExecuteAsync("SELECT pg_notify('outbox', 'REALTIME')", null, cancellationToken);
            }

            await db.CommitAsync(cancellationToken);
            return expired;
        }
        catch
        {
            await db.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Deletes expired idempotency keys, long-expired refresh tokens and old processed outbox rows (batched).</summary>
    public async Task<HousekeepingPurgeResult> PurgeAsync(DateTime now, int batchSize, CancellationToken cancellationToken)
    {
        var keys = await db.ExecuteAsync(
            """
            DELETE FROM idempotency_keys WHERE id IN (
                SELECT id FROM idempotency_keys WHERE expires_at <= @now ORDER BY expires_at LIMIT @batchSize)
            """,
            new { now, batchSize }, cancellationToken);

        var tokens = await db.ExecuteAsync(
            """
            DELETE FROM refresh_tokens WHERE id IN (
                SELECT id FROM refresh_tokens WHERE family_expires_at <= @cutoff ORDER BY family_expires_at LIMIT @batchSize)
            """,
            new { cutoff = now - RefreshTokenRetention, batchSize }, cancellationToken);

        var outbox = await db.ExecuteAsync(
            """
            DELETE FROM outbox_messages WHERE id IN (
                SELECT id FROM outbox_messages WHERE status = 'PROCESSED' AND processed_at <= @cutoff ORDER BY processed_at LIMIT @batchSize)
            """,
            new { cutoff = now - ProcessedOutboxRetention, batchSize }, cancellationToken);

        return new HousekeepingPurgeResult(keys, tokens, outbox);
    }
}

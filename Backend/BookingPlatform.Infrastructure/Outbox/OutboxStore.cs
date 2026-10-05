using Dapper;
using Npgsql;

namespace BookingPlatform.Infrastructure.Outbox;

public sealed record ClaimedOutboxMessage(Guid Id, Guid? TenantId, string Type, string Payload, int AttemptCount, DateTime CreatedAt);

/// <summary>
/// Claims and settles outbox rows for one destination (DATABASE.md §7). Claims use <c>FOR UPDATE SKIP LOCKED</c> so any
/// number of dispatcher instances share the work, and a lease so rows of a crashed instance are reclaimed.
/// </summary>
public sealed class OutboxStore(NpgsqlDataSource dataSource)
{
    public const int MaxErrorLength = 2000;

    public async Task<IReadOnlyList<ClaimedOutboxMessage>> ClaimAsync(
        string destination, int batchSize, TimeSpan lease, string lockedBy, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ClaimedOutboxMessage>(new CommandDefinition(
            """
            WITH claimed AS (
                SELECT id FROM outbox_messages
                WHERE destination = @destination
                  AND ((status IN ('PENDING', 'FAILED') AND available_at <= now())
                       OR (status = 'PROCESSING' AND locked_until < now()))
                ORDER BY available_at, id
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED)
            UPDATE outbox_messages o
            SET status = 'PROCESSING', attempt_count = o.attempt_count + 1,
                locked_until = now() + make_interval(secs => @leaseSeconds), locked_by = @lockedBy
            FROM claimed
            WHERE o.id = claimed.id
            RETURNING o.id, o.tenant_id, o.type, o.payload::text AS payload, o.attempt_count, o.created_at
            """,
            new { destination, batchSize, leaseSeconds = lease.TotalSeconds, lockedBy },
            cancellationToken: cancellationToken));
        return rows.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();
    }

    public async Task MarkProcessedAsync(Guid id, string? note, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE outbox_messages
            SET status = 'PROCESSED', processed_at = now(), locked_until = NULL, locked_by = NULL, last_error = @note
            WHERE id = @id
            """,
            new { id, note = Truncate(note) },
            cancellationToken: cancellationToken));
    }

    /// <summary>Schedules a retry after <paramref name="retryAfter"/>, or dead-letters the row when <paramref name="dead"/>.</summary>
    public async Task MarkFailedAsync(Guid id, string error, TimeSpan retryAfter, bool dead, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE outbox_messages
            SET status = CASE WHEN @dead THEN 'DEAD' ELSE 'FAILED' END,
                available_at = now() + make_interval(secs => @retrySeconds),
                locked_until = NULL, locked_by = NULL, last_error = @error
            WHERE id = @id
            """,
            new { id, error = Truncate(error), retrySeconds = retryAfter.TotalSeconds, dead },
            cancellationToken: cancellationToken));
    }

    private static string? Truncate(string? value) =>
        value is null || value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
}

using BookingPlatform.Application.Bookings;
using BookingPlatform.Infrastructure.Persistence;
using Npgsql;

namespace BookingPlatform.Infrastructure.Bookings;

public sealed class AllocationRepository(DbSession db) : IAllocationRepository
{
    public const string NoOverlapConstraint = "ex_booking_allocations_no_overlap";

    /// <summary>First key of the two-key advisory lock space, reserved for resource allocation.</summary>
    private const int AllocationLockClass = 0x424B;

    public Task LockResourcesAsync(IReadOnlyCollection<Guid> resourceIds, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            SELECT pg_advisory_xact_lock(@lockClass, hashtext(r::text))
            FROM (SELECT DISTINCT r FROM unnest(@resourceIds) AS r ORDER BY r OFFSET 0) AS ordered
            """,
            new { lockClass = AllocationLockClass, resourceIds = resourceIds.ToArray() },
            cancellationToken);

    public Task ReleaseExpiredHoldsAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds, DateTime from, DateTime to, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            WITH released AS (
                UPDATE booking_allocations
                SET is_active = false, released_at = @now, release_reason = 'HOLD_EXPIRED'
                WHERE tenant_id = @tenantId AND allocation_type = 'HOLD' AND is_active AND expires_at <= @now
                  AND resource_id = ANY(@resourceIds) AND period && tstzrange(@from, @to, '[)')
                RETURNING hold_id)
            UPDATE resource_holds
            SET status = 'EXPIRED', released_at = @now, updated_at = @now
            WHERE tenant_id = @tenantId AND status = 'ACTIVE' AND id IN (SELECT hold_id FROM released)
            """,
            new { tenantId, resourceIds = resourceIds.ToArray(), from, to, now },
            cancellationToken);

    public Task<bool> TryInsertAsync(AllocationInsert insert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(insert);
        return db.TryExecuteAsync(
            """
            INSERT INTO booking_allocations (tenant_id, venue_id, resource_id, allocation_type, booking_id, hold_id, block_id, starts_at, ends_at, expires_at)
            SELECT @TenantId, @VenueId, r, @AllocationType, @BookingId, @HoldId, @BlockId, @StartsAt, @EndsAt, @ExpiresAt
            FROM unnest(@ResourceIds) AS r
            ORDER BY r
            """,
            new
            {
                insert.TenantId,
                insert.VenueId,
                insert.AllocationType,
                insert.BookingId,
                insert.HoldId,
                insert.BlockId,
                insert.StartsAt,
                insert.EndsAt,
                insert.ExpiresAt,
                ResourceIds = insert.ResourceIds.ToArray(),
            },
            ex => ex.SqlState == PostgresErrorCodes.ExclusionViolation && ex.ConstraintName == NoOverlapConstraint,
            cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> FindConflictingResourcesAsync(Guid tenantId, IReadOnlyCollection<Guid> resourceIds, DateTime from, DateTime to, DateTime now, CancellationToken cancellationToken) =>
        db.QueryAsync<Guid>(
            """
            SELECT DISTINCT resource_id FROM booking_allocations
            WHERE tenant_id = @tenantId AND resource_id = ANY(@resourceIds) AND is_active
              AND period && tstzrange(@from, @to, '[)')
              AND NOT (allocation_type = 'HOLD' AND expires_at <= @now)
            ORDER BY resource_id
            """,
            new { tenantId, resourceIds = resourceIds.ToArray(), from, to, now },
            cancellationToken);

    public Task<IReadOnlyList<Guid>> ListActiveResourceIdsAsync(Guid tenantId, Guid? bookingId, Guid? holdId, Guid? blockId, CancellationToken cancellationToken) =>
        db.QueryAsync<Guid>(
            $"SELECT resource_id FROM booking_allocations WHERE tenant_id = @tenantId AND is_active AND {OwnerFilter} ORDER BY resource_id",
            new { tenantId, bookingId, holdId, blockId },
            cancellationToken);

    public Task DeactivateAsync(Guid tenantId, Guid? bookingId, Guid? holdId, Guid? blockId, string reason, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            $"""
            UPDATE booking_allocations SET is_active = false, released_at = @now, release_reason = @reason
            WHERE tenant_id = @tenantId AND is_active AND {OwnerFilter}
            """,
            new { tenantId, bookingId, holdId, blockId, reason, now },
            cancellationToken);

    public Task TruncateBookingAsync(Guid tenantId, Guid bookingId, DateTime endsAt, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE booking_allocations SET ends_at = @endsAt
            WHERE tenant_id = @tenantId AND booking_id = @bookingId AND is_active AND ends_at > @endsAt AND starts_at < @endsAt
            """,
            new { tenantId, bookingId, endsAt },
            cancellationToken);

    private const string OwnerFilter = """
        ((@bookingId::uuid IS NOT NULL AND booking_id = @bookingId)
          OR (@holdId::uuid IS NOT NULL AND hold_id = @holdId)
          OR (@blockId::uuid IS NOT NULL AND block_id = @blockId))
        """;
}

public sealed class HoldRepository(DbSession db) : IHoldRepository
{
    private const string Columns = """
        id, venue_id, owner_type, owner_reference, status, party_size, starts_at, ends_at, expires_at, converted_booking_id
        """;

    public Task InsertAsync(Guid tenantId, Guid holdId, Guid venueId, string ownerType, string ownerReference, byte[] tokenHash, int partySize,
        DateTime startsAt, DateTime endsAt, DateTime expiresAt, Guid? userId, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            INSERT INTO resource_holds (id, tenant_id, venue_id, owner_type, owner_reference, token_hash, party_size, starts_at, ends_at, expires_at, created_by_user_id)
            VALUES (@holdId, @tenantId, @venueId, @ownerType, @ownerReference, @tokenHash, @partySize, @startsAt, @endsAt, @expiresAt, @userId)
            """,
            new { holdId, tenantId, venueId, ownerType, ownerReference, tokenHash, partySize, startsAt, endsAt, expiresAt, userId },
            cancellationToken);

    public Task<HoldRecord?> GetAsync(Guid tenantId, Guid holdId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<HoldRecord>($"SELECT {Columns} FROM resource_holds WHERE tenant_id = @tenantId AND id = @holdId",
            new { tenantId, holdId }, cancellationToken);

    public Task<HoldRecord?> LockByTokenAsync(Guid tenantId, byte[] tokenHash, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<HoldRecord>($"SELECT {Columns} FROM resource_holds WHERE tenant_id = @tenantId AND token_hash = @tokenHash FOR UPDATE",
            new { tenantId, tokenHash }, cancellationToken);

    public Task<HoldRecord?> LockAsync(Guid tenantId, Guid holdId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<HoldRecord>($"SELECT {Columns} FROM resource_holds WHERE tenant_id = @tenantId AND id = @holdId FOR UPDATE",
            new { tenantId, holdId }, cancellationToken);

    public Task SetStatusAsync(Guid tenantId, Guid holdId, string status, Guid? convertedBookingId, DateTime now, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE resource_holds
            SET status = @status, converted_booking_id = @convertedBookingId, updated_at = @now,
                released_at = CASE WHEN @status IN ('RELEASED', 'EXPIRED') THEN @now ELSE released_at END
            WHERE tenant_id = @tenantId AND id = @holdId
            """,
            new { tenantId, holdId, status, convertedBookingId, now },
            cancellationToken);
}

public sealed class BlockRepository(DbSession db) : IBlockRepository
{
    private const string Columns = """
        id, venue_id, reason, notes, starts_at, ends_at, status, created_by_user_id, created_at, cancelled_at
        """;

    public Task InsertAsync(Guid tenantId, Guid blockId, CreateBlockRequest request, Guid? userId, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            INSERT INTO resource_blocks (id, tenant_id, venue_id, reason, notes, starts_at, ends_at, created_by_user_id)
            VALUES (@blockId, @tenantId, @VenueId, @Reason, @Notes, @StartsAt, @EndsAt, @userId)
            """,
            new { blockId, tenantId, request.VenueId, request.Reason, request.Notes, request.StartsAt, request.EndsAt, userId },
            cancellationToken);

    public async Task<BlockDto?> GetAsync(Guid tenantId, Guid blockId, CancellationToken cancellationToken)
    {
        var rows = await LoadAsync($"SELECT {Columns} FROM resource_blocks WHERE tenant_id = @tenantId AND id = @blockId",
            new { tenantId, blockId }, tenantId, cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<IReadOnlyList<BlockDto>> ListAsync(Guid tenantId, Guid venueId, DateTime? from, DateTime? to, bool includeCancelled, CancellationToken cancellationToken) =>
        LoadAsync(
            $"""
            SELECT {Columns} FROM resource_blocks
            WHERE tenant_id = @tenantId AND venue_id = @venueId
              AND (@includeCancelled OR status = 'ACTIVE')
              AND (@from::timestamptz IS NULL OR ends_at > @from)
              AND (@to::timestamptz IS NULL OR starts_at < @to)
            ORDER BY starts_at, id
            LIMIT 500
            """,
            new { tenantId, venueId, from, to, includeCancelled }, tenantId, cancellationToken);

    public async Task<bool> CancelAsync(Guid tenantId, Guid blockId, Guid? userId, DateTime now, CancellationToken cancellationToken) =>
        await db.ExecuteAsync(
            """
            UPDATE resource_blocks SET status = 'CANCELLED', cancelled_at = @now, cancelled_by_user_id = @userId, updated_at = @now
            WHERE tenant_id = @tenantId AND id = @blockId AND status = 'ACTIVE'
            """,
            new { tenantId, blockId, userId, now },
            cancellationToken) == 1;

    private async Task<IReadOnlyList<BlockDto>> LoadAsync(string sql, object parameters, Guid tenantId, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<BlockRow>(sql, parameters, cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var members = await db.QueryAsync<MemberRow>(
            "SELECT DISTINCT block_id, resource_id FROM booking_allocations WHERE tenant_id = @tenantId AND block_id = ANY(@ids)",
            new { tenantId, ids = rows.Select(r => r.Id).ToArray() }, cancellationToken);
        var byBlock = members.ToLookup(m => m.BlockId, m => m.ResourceId);
        return rows.Select(r => new BlockDto(r.Id, r.VenueId, r.Reason, r.Notes, r.StartsAt, r.EndsAt, r.Status,
            byBlock[r.Id].Order().ToList(), r.CreatedByUserId, r.CreatedAt, r.CancelledAt)).ToList();
    }

    private sealed class BlockRow
    {
        public Guid Id { get; init; }
        public Guid VenueId { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string? Notes { get; init; }
        public DateTime StartsAt { get; init; }
        public DateTime EndsAt { get; init; }
        public string Status { get; init; } = string.Empty;
        public Guid? CreatedByUserId { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? CancelledAt { get; init; }
    }

    private sealed class MemberRow
    {
        public Guid BlockId { get; init; }
        public Guid ResourceId { get; init; }
    }
}

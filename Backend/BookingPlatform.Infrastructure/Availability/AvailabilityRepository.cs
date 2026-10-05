using BookingPlatform.Application.Availability;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Availability;

public sealed class AvailabilityRepository(DbSession db) : IAvailabilityRepository
{
    public Task<IReadOnlyList<AvailabilityResourceRow>> ListResourcesAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken) =>
        db.QueryAsync<AvailabilityResourceRow>(
            """
            SELECT id, name, section_id, resource_type_id, min_capacity, capacity, is_active, is_bookable_online, sort_order
            FROM bookable_resources
            WHERE tenant_id = @tenantId AND venue_id = @venueId
            ORDER BY sort_order, name, id
            """,
            new { tenantId, venueId },
            cancellationToken);

    public async Task<IReadOnlyList<AvailabilityCombinationRow>> ListActiveCombinationsAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken)
    {
        var combinations = await db.QueryAsync<CombinationRow>(
            """
            SELECT id, name, min_capacity, capacity, is_bookable_online, sort_order
            FROM resource_combinations
            WHERE tenant_id = @tenantId AND venue_id = @venueId AND is_active
            ORDER BY sort_order, name, id
            """,
            new { tenantId, venueId },
            cancellationToken);
        if (combinations.Count == 0)
        {
            return [];
        }

        var members = await db.QueryAsync<MemberRow>(
            """
            SELECT combination_id, resource_id, is_required FROM resource_combination_members
            WHERE tenant_id = @tenantId AND combination_id = ANY(@ids)
            """,
            new { tenantId, ids = combinations.Select(c => c.Id).ToArray() },
            cancellationToken);
        var byCombination = members.ToLookup(m => m.CombinationId);
        return combinations
            .Select(c => new AvailabilityCombinationRow(c.Id, c.Name, c.MinCapacity, c.Capacity, c.IsBookableOnline, c.SortOrder,
                byCombination[c.Id].Select(m => (m.ResourceId, m.IsRequired)).ToList()))
            .ToList();
    }

    public Task<IReadOnlyList<ClaimRow>> ListClaimsAsync(
        Guid tenantId, Guid venueId, DateTime from, DateTime to, DateTime now, Guid? excludeHoldId, CancellationToken cancellationToken) =>
        db.QueryAsync<ClaimRow>(
            """
            SELECT resource_id, starts_at, ends_at
            FROM booking_allocations
            WHERE tenant_id = @tenantId AND venue_id = @venueId AND is_active
              AND period && tstzrange(@from, @to, '[)')
              AND NOT (allocation_type = 'HOLD' AND expires_at <= @now)
              AND (@excludeHoldId::uuid IS NULL OR hold_id IS DISTINCT FROM @excludeHoldId)
            """,
            new { tenantId, venueId, from, to, now, excludeHoldId },
            cancellationToken);

    private sealed class CombinationRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int MinCapacity { get; init; }
        public int Capacity { get; init; }
        public bool IsBookableOnline { get; init; }
        public int SortOrder { get; init; }
    }

    private sealed class MemberRow
    {
        public Guid CombinationId { get; init; }
        public Guid ResourceId { get; init; }
        public bool IsRequired { get; init; }
    }
}

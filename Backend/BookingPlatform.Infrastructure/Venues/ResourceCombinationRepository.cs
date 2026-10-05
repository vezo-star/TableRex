using BookingPlatform.Application.Venues;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Venues;

public sealed class ResourceCombinationRepository(DbSession db) : IResourceCombinationRepository
{
    private const string Columns = "id, venue_id, name, min_capacity, capacity, is_active, is_bookable_online, sort_order, created_at, updated_at";

    public async Task<IReadOnlyList<ResourceCombinationDto>> ListAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<CombinationRow>(
            $"SELECT {Columns} FROM resource_combinations WHERE tenant_id = @tenantId AND venue_id = @venueId ORDER BY sort_order, name, id",
            new { tenantId, venueId },
            cancellationToken);
        return await WithMembersAsync(tenantId, rows, cancellationToken);
    }

    public async Task<ResourceCombinationDto?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<CombinationRow>(
            $"SELECT {Columns} FROM resource_combinations WHERE tenant_id = @tenantId AND id = @id",
            new { tenantId, id },
            cancellationToken);
        return row is null ? null : (await WithMembersAsync(tenantId, [row], cancellationToken))[0];
    }

    public async Task<bool> NameExistsAsync(Guid tenantId, Guid venueId, string name, Guid? exceptId, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM resource_combinations
                           WHERE tenant_id = @tenantId AND venue_id = @venueId AND lower(name) = lower(@name)
                             AND (@exceptId::uuid IS NULL OR id <> @exceptId))
            """,
            new { tenantId, venueId, name, exceptId },
            cancellationToken);

    public async Task<Guid> CreateAsync(Guid tenantId, CreateResourceCombinationRequest request, CancellationToken cancellationToken)
    {
        var id = await db.QuerySingleAsync<Guid>(
            """
            INSERT INTO resource_combinations (tenant_id, venue_id, name, min_capacity, capacity, is_bookable_online, sort_order)
            VALUES (@tenantId, @VenueId, @Name, @MinCapacity, @Capacity, @IsBookableOnline, @SortOrder)
            RETURNING id
            """,
            new { tenantId, request.VenueId, request.Name, request.MinCapacity, request.Capacity, request.IsBookableOnline, request.SortOrder },
            cancellationToken);
        await InsertMembersAsync(tenantId, request.VenueId, id, request.Members, cancellationToken);
        return id;
    }

    public async Task UpdateAsync(Guid tenantId, Guid venueId, Guid id, UpdateResourceCombinationRequest request, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync(
            """
            UPDATE resource_combinations SET name = @Name, min_capacity = @MinCapacity, capacity = @Capacity,
                is_active = @IsActive, is_bookable_online = @IsBookableOnline, sort_order = @SortOrder, updated_at = now()
            WHERE tenant_id = @tenantId AND id = @id
            """,
            new { tenantId, id, request.Name, request.MinCapacity, request.Capacity, request.IsActive, request.IsBookableOnline, request.SortOrder },
            cancellationToken);
        await db.ExecuteAsync(
            "DELETE FROM resource_combination_members WHERE tenant_id = @tenantId AND combination_id = @id",
            new { tenantId, id },
            cancellationToken);
        await InsertMembersAsync(tenantId, venueId, id, request.Members, cancellationToken);
    }

    public async Task<DeleteResult> DeleteOrDeactivateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var parameters = new { tenantId, id };
        if (await db.TryDeleteAsync("DELETE FROM resource_combinations WHERE tenant_id = @tenantId AND id = @id", parameters, cancellationToken))
        {
            return new DeleteResult(Deleted: true, Deactivated: false);
        }

        await db.ExecuteAsync(
            "UPDATE resource_combinations SET is_active = false, updated_at = now() WHERE tenant_id = @tenantId AND id = @id",
            parameters,
            cancellationToken);
        return new DeleteResult(Deleted: false, Deactivated: true);
    }

    private Task InsertMembersAsync(Guid tenantId, Guid venueId, Guid id, IReadOnlyList<CombinationMemberDto> members, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            INSERT INTO resource_combination_members (tenant_id, venue_id, combination_id, resource_id, is_required)
            SELECT @tenantId, @venueId, @id, r, q FROM unnest(@resourceIds, @required) AS m(r, q)
            """,
            new
            {
                tenantId,
                venueId,
                id,
                resourceIds = members.Select(m => m.ResourceId).ToArray(),
                required = members.Select(m => m.IsRequired).ToArray(),
            },
            cancellationToken);

    private async Task<IReadOnlyList<ResourceCombinationDto>> WithMembersAsync(Guid tenantId, IReadOnlyList<CombinationRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var members = await db.QueryAsync<MemberRow>(
            """
            SELECT m.combination_id, m.resource_id, m.is_required
            FROM resource_combination_members m
            JOIN bookable_resources r ON r.id = m.resource_id
            WHERE m.tenant_id = @tenantId AND m.combination_id = ANY(@ids)
            ORDER BY r.sort_order, r.name, r.id
            """,
            new { tenantId, ids = rows.Select(r => r.Id).ToArray() },
            cancellationToken);
        var byCombination = members.ToLookup(m => m.CombinationId);
        return rows.Select(r => new ResourceCombinationDto(
            r.Id, r.VenueId, r.Name, r.MinCapacity, r.Capacity, r.IsActive, r.IsBookableOnline, r.SortOrder,
            byCombination[r.Id].Select(m => new CombinationMemberDto(m.ResourceId, m.IsRequired)).ToList(),
            r.CreatedAt, r.UpdatedAt)).ToList();
    }

    private sealed class CombinationRow
    {
        public Guid Id { get; init; }
        public Guid VenueId { get; init; }
        public string Name { get; init; } = string.Empty;
        public int MinCapacity { get; init; }
        public int Capacity { get; init; }
        public bool IsActive { get; init; }
        public bool IsBookableOnline { get; init; }
        public int SortOrder { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class MemberRow
    {
        public Guid CombinationId { get; init; }
        public Guid ResourceId { get; init; }
        public bool IsRequired { get; init; }
    }
}

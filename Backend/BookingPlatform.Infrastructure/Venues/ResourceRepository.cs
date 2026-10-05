using System.Text.Json;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Venues;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Venues;

public sealed class ResourceRepository(DbSession db) : IResourceRepository
{
    private const string TypeColumns = "id, code, name, is_active";

    private const string ResourceColumns = """
        id, venue_id, section_id, resource_type_id, name, code, min_capacity, capacity, is_active, is_bookable_online,
        sort_order, attributes::text AS attributes, created_at, updated_at
        """;

    public Task<IReadOnlyList<ResourceTypeDto>> ListTypesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.QueryAsync<ResourceTypeDto>(
            $"SELECT {TypeColumns} FROM resource_types WHERE tenant_id = @tenantId ORDER BY name, code",
            new { tenantId },
            cancellationToken);

    public Task<ResourceTypeDto?> GetTypeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<ResourceTypeDto>(
            $"SELECT {TypeColumns} FROM resource_types WHERE tenant_id = @tenantId AND id = @id",
            new { tenantId, id },
            cancellationToken);

    public async Task<bool> TypeCodeExistsAsync(Guid tenantId, string code, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM resource_types WHERE tenant_id = @tenantId AND code = @code)",
            new { tenantId, code },
            cancellationToken);

    public Task<Guid> CreateTypeAsync(Guid tenantId, CreateResourceTypeRequest request, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            "INSERT INTO resource_types (tenant_id, code, name) VALUES (@tenantId, @Code, @Name) RETURNING id",
            new { tenantId, request.Code, request.Name },
            cancellationToken);

    public Task UpdateTypeAsync(Guid tenantId, Guid id, UpdateResourceTypeRequest request, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "UPDATE resource_types SET name = @Name, is_active = @IsActive, updated_at = now() WHERE tenant_id = @tenantId AND id = @id",
            new { tenantId, id, request.Name, request.IsActive },
            cancellationToken);

    public async Task<PagedResult<ResourceDto>> ListAsync(Guid tenantId, ResourceQuery query, IReadOnlyCollection<Guid>? venueIds, CancellationToken cancellationToken)
    {
        const string filter = """
            WHERE tenant_id = @tenantId
              AND (@allVenues OR venue_id = ANY(@venueIds))
              AND (@sectionId::uuid IS NULL OR section_id = @sectionId)
              AND (@resourceTypeId::uuid IS NULL OR resource_type_id = @resourceTypeId)
              AND (@isActive::boolean IS NULL OR is_active = @isActive)
            """;

        await using var grid = await db.QueryMultipleAsync(
            $"""
            SELECT count(*) FROM bookable_resources {filter};
            SELECT {ResourceColumns} FROM bookable_resources {filter}
            ORDER BY venue_id, sort_order, name, id LIMIT @PageSize OFFSET @Offset;
            """,
            new
            {
                tenantId,
                allVenues = venueIds is null,
                venueIds = venueIds?.ToArray() ?? [],
                sectionId = query.SectionId,
                resourceTypeId = query.ResourceTypeId,
                isActive = query.IsActive,
                query.Page.PageSize,
                query.Page.Offset,
            },
            cancellationToken);

        var total = await grid.ReadSingleAsync<long>();
        var rows = await grid.ReadAsync<ResourceRow>();
        return new PagedResult<ResourceDto>(rows.Select(Map).ToList(), query.Page.Page, query.Page.PageSize, total);
    }

    public async Task<IReadOnlyList<ResourceDto>> ListByVenueAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<ResourceRow>(
            $"SELECT {ResourceColumns} FROM bookable_resources WHERE tenant_id = @tenantId AND venue_id = @venueId ORDER BY sort_order, name, id",
            new { tenantId, venueId },
            cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<ResourceDto?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<ResourceRow>(
            $"SELECT {ResourceColumns} FROM bookable_resources WHERE tenant_id = @tenantId AND id = @id",
            new { tenantId, id },
            cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<bool> CodeExistsAsync(Guid tenantId, Guid venueId, string code, Guid? exceptId, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM bookable_resources
                           WHERE tenant_id = @tenantId AND venue_id = @venueId AND code = @code
                             AND (@exceptId::uuid IS NULL OR id <> @exceptId))
            """,
            new { tenantId, venueId, code, exceptId },
            cancellationToken);

    public Task<int> CountAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<int>("SELECT count(*)::int FROM bookable_resources WHERE tenant_id = @tenantId", new { tenantId }, cancellationToken);

    public Task<Guid> CreateAsync(Guid tenantId, CreateResourceRequest request, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            """
            INSERT INTO bookable_resources (tenant_id, venue_id, section_id, resource_type_id, name, code, min_capacity,
                                            capacity, is_bookable_online, sort_order, attributes)
            VALUES (@tenantId, @VenueId, @SectionId, @ResourceTypeId, @Name, @Code, @MinCapacity, @Capacity,
                    @IsBookableOnline, @SortOrder, @attributes::jsonb)
            RETURNING id
            """,
            new
            {
                tenantId, request.VenueId, request.SectionId, request.ResourceTypeId, request.Name, request.Code,
                request.MinCapacity, request.Capacity, request.IsBookableOnline, request.SortOrder,
                attributes = SerializeAttributes(request.Attributes),
            },
            cancellationToken);

    public Task UpdateAsync(Guid tenantId, Guid id, UpdateResourceRequest request, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE bookable_resources SET
                section_id = @SectionId, resource_type_id = @ResourceTypeId, name = @Name, code = @Code,
                min_capacity = @MinCapacity, capacity = @Capacity, is_active = @IsActive,
                is_bookable_online = @IsBookableOnline, sort_order = @SortOrder, attributes = @attributes::jsonb,
                updated_at = now()
            WHERE tenant_id = @tenantId AND id = @id
            """,
            new
            {
                tenantId, id, request.SectionId, request.ResourceTypeId, request.Name, request.Code, request.MinCapacity,
                request.Capacity, request.IsActive, request.IsBookableOnline, request.SortOrder,
                attributes = SerializeAttributes(request.Attributes),
            },
            cancellationToken);

    public async Task<DeleteResult> DeleteOrDeactivateAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var parameters = new { tenantId, id };
        if (await db.TryDeleteAsync("DELETE FROM bookable_resources WHERE tenant_id = @tenantId AND id = @id", parameters, cancellationToken))
        {
            return new DeleteResult(Deleted: true, Deactivated: false);
        }

        await db.ExecuteAsync(
            "UPDATE bookable_resources SET is_active = false, updated_at = now() WHERE tenant_id = @tenantId AND id = @id",
            parameters,
            cancellationToken);
        return new DeleteResult(Deleted: false, Deactivated: true);
    }

    private static string SerializeAttributes(IReadOnlyDictionary<string, string>? attributes) =>
        JsonSerializer.Serialize(attributes ?? new Dictionary<string, string>());

    private static ResourceDto Map(ResourceRow r) => new(
        r.Id, r.VenueId, r.SectionId, r.ResourceTypeId, r.Name, r.Code, r.MinCapacity, r.Capacity, r.IsActive,
        r.IsBookableOnline, r.SortOrder,
        JsonSerializer.Deserialize<Dictionary<string, string>>(r.Attributes) ?? new Dictionary<string, string>(),
        r.CreatedAt, r.UpdatedAt);

    private sealed class ResourceRow
    {
        public Guid Id { get; init; }
        public Guid VenueId { get; init; }
        public Guid? SectionId { get; init; }
        public Guid ResourceTypeId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public int MinCapacity { get; init; }
        public int Capacity { get; init; }
        public bool IsActive { get; init; }
        public bool IsBookableOnline { get; init; }
        public int SortOrder { get; init; }
        public string Attributes { get; init; } = "{}";
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}

public sealed class FloorPlanRepository(DbSession db) : IFloorPlanRepository
{
    private const string PlanColumns = "id, venue_id, name, width, height, sort_order, is_active, updated_at";

    public async Task<IReadOnlyList<FloorPlanDto>> ListAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken)
    {
        var plans = await db.QueryAsync<PlanRow>(
            $"SELECT {PlanColumns} FROM floor_plans WHERE tenant_id = @tenantId AND venue_id = @venueId ORDER BY sort_order, name, id",
            new { tenantId, venueId },
            cancellationToken);
        if (plans.Count == 0)
        {
            return [];
        }

        var positions = await db.QueryAsync<PositionRow>(
            """
            SELECT floor_plan_id, resource_id, x, y, width, height, rotation, shape FROM floor_plan_resources
            WHERE tenant_id = @tenantId AND floor_plan_id = ANY(@ids)
            """,
            new { tenantId, ids = plans.Select(p => p.Id).ToArray() },
            cancellationToken);
        var byPlan = positions.ToLookup(p => p.FloorPlanId);
        return plans.Select(p => Map(p, byPlan[p.Id])).ToList();
    }

    public async Task<FloorPlanDto?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var plan = await db.QuerySingleOrDefaultAsync<PlanRow>(
            $"SELECT {PlanColumns} FROM floor_plans WHERE tenant_id = @tenantId AND id = @id",
            new { tenantId, id },
            cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var positions = await db.QueryAsync<PositionRow>(
            """
            SELECT floor_plan_id, resource_id, x, y, width, height, rotation, shape FROM floor_plan_resources
            WHERE tenant_id = @tenantId AND floor_plan_id = @id
            """,
            new { tenantId, id },
            cancellationToken);
        return Map(plan, positions);
    }

    public Task<Guid> CreateAsync(Guid tenantId, CreateFloorPlanRequest request, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            """
            INSERT INTO floor_plans (tenant_id, venue_id, name, width, height, sort_order)
            VALUES (@tenantId, @VenueId, @Name, @Width, @Height, @SortOrder)
            RETURNING id
            """,
            new { tenantId, request.VenueId, request.Name, request.Width, request.Height, request.SortOrder },
            cancellationToken);

    public Task UpdateAsync(Guid tenantId, Guid id, UpdateFloorPlanRequest request, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            """
            UPDATE floor_plans SET name = @Name, width = @Width, height = @Height, sort_order = @SortOrder,
                                   is_active = @IsActive, updated_at = now()
            WHERE tenant_id = @tenantId AND id = @id
            """,
            new { tenantId, id, request.Name, request.Width, request.Height, request.SortOrder, request.IsActive },
            cancellationToken);

    public Task DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.ExecuteAsync("DELETE FROM floor_plans WHERE tenant_id = @tenantId AND id = @id", new { tenantId, id }, cancellationToken);

    public async Task ReplacePositionsAsync(Guid tenantId, Guid venueId, Guid id, IReadOnlyList<FloorPlanPositionDto> positions, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync(
            "DELETE FROM floor_plan_resources WHERE tenant_id = @tenantId AND floor_plan_id = @id",
            new { tenantId, id },
            cancellationToken);
        if (positions.Count > 0)
        {
            await db.ExecuteAsync(
                """
                INSERT INTO floor_plan_resources (tenant_id, venue_id, floor_plan_id, resource_id, x, y, width, height, rotation, shape)
                SELECT @tenantId, @venueId, @id, r, x, y, w, h, rot, s
                FROM unnest(@resourceIds, @xs, @ys, @widths, @heights, @rotations, @shapes) AS p(r, x, y, w, h, rot, s)
                """,
                new
                {
                    tenantId,
                    venueId,
                    id,
                    resourceIds = positions.Select(p => p.ResourceId).ToArray(),
                    xs = positions.Select(p => p.X).ToArray(),
                    ys = positions.Select(p => p.Y).ToArray(),
                    widths = positions.Select(p => p.Width).ToArray(),
                    heights = positions.Select(p => p.Height).ToArray(),
                    rotations = positions.Select(p => p.Rotation).ToArray(),
                    shapes = positions.Select(p => p.Shape).ToArray(),
                },
                cancellationToken);
        }

        await db.ExecuteAsync("UPDATE floor_plans SET updated_at = now() WHERE tenant_id = @tenantId AND id = @id", new { tenantId, id }, cancellationToken);
    }

    private static FloorPlanDto Map(PlanRow p, IEnumerable<PositionRow> positions) => new(
        p.Id, p.VenueId, p.Name, p.Width, p.Height, p.SortOrder, p.IsActive,
        positions.Select(x => new FloorPlanPositionDto(x.ResourceId, x.X, x.Y, x.Width, x.Height, x.Rotation, x.Shape)).ToList(),
        p.UpdatedAt);

    private sealed class PlanRow
    {
        public Guid Id { get; init; }
        public Guid VenueId { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Width { get; init; }
        public int Height { get; init; }
        public int SortOrder { get; init; }
        public bool IsActive { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class PositionRow
    {
        public Guid FloorPlanId { get; init; }
        public Guid ResourceId { get; init; }
        public decimal X { get; init; }
        public decimal Y { get; init; }
        public decimal Width { get; init; }
        public decimal Height { get; init; }
        public decimal Rotation { get; init; }
        public string Shape { get; init; } = string.Empty;
    }
}

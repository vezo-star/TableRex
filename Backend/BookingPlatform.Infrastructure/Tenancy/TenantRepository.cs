using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Tenancy;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Tenancy;

public sealed class TenantRepository(DbSession db) : ITenantRepository
{
    private const string Columns = """
        id, name, slug, status, default_time_zone, created_at, updated_at,
        coalesce((SELECT s.plan_code FROM tenant_subscriptions s WHERE s.tenant_id = tenants.id),
                 (SELECT p.code FROM plans p WHERE p.is_default)) AS plan_code
        """;

    public Task<TenantDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<TenantDto>($"SELECT {Columns} FROM tenants WHERE id = @tenantId", new { tenantId }, cancellationToken);

    // NO KEY UPDATE serialises callers without blocking concurrent inserts whose foreign keys reference the tenant row.
    public Task LockAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.ExecuteAsync("SELECT 1 FROM tenants WHERE id = @tenantId FOR NO KEY UPDATE", new { tenantId }, cancellationToken);

    public Task UpdateAsync(Guid tenantId, string name, string defaultTimeZone, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "UPDATE tenants SET name = @name, default_time_zone = @defaultTimeZone, updated_at = now() WHERE id = @tenantId",
            new { tenantId, name, defaultTimeZone },
            cancellationToken);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM tenants WHERE slug = @slug)", new { slug }, cancellationToken);

    public Task<Guid> CreateAsync(string name, string slug, string defaultTimeZone, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            "INSERT INTO tenants (name, slug, default_time_zone) VALUES (@name, @slug, @defaultTimeZone) RETURNING id",
            new { name, slug, defaultTimeZone },
            cancellationToken);

    public async Task<PagedResult<TenantDto>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        await using var grid = await db.QueryMultipleAsync(
            $"""
            SELECT count(*) FROM tenants;
            SELECT {Columns} FROM tenants ORDER BY name, id LIMIT @PageSize OFFSET @Offset;
            """,
            new { page.PageSize, page.Offset },
            cancellationToken);
        var total = await grid.ReadSingleAsync<long>();
        var items = (await grid.ReadAsync<TenantDto>()).ToList();
        return new PagedResult<TenantDto>(items, page.Page, page.PageSize, total);
    }
}

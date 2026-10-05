using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Tenancy;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Tenancy;

public sealed class StaffRepository(DbSession db) : IStaffRepository
{
    private const string SelectStaff = """
        SELECT tu.id AS tenant_user_id, u.id AS user_id, u.email, u.display_name, tu.status, tu.created_at,
               COALESCE((SELECT array_agg(r.code ORDER BY r.code)
                         FROM tenant_user_roles tur JOIN roles r ON r.id = tur.role_id
                         WHERE tur.tenant_user_id = tu.id), '{}') AS roles,
               '{}'::uuid[] AS venue_ids
        FROM tenant_users tu
        JOIN users u ON u.id = tu.user_id
        """;

    public async Task<PagedResult<StaffMemberDto>> ListAsync(Guid tenantId, StaffQuery query, CancellationToken cancellationToken)
    {
        const string filter = """
            WHERE tu.tenant_id = @tenantId
              AND (@status::text IS NULL OR tu.status = @status)
              AND (@search::text IS NULL OR u.email ILIKE @search OR u.display_name ILIKE @search)
            """;
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : $"%{EscapeLike(query.Search.Trim())}%";

        await using var grid = await db.QueryMultipleAsync(
            $"""
            SELECT count(*) FROM tenant_users tu JOIN users u ON u.id = tu.user_id {filter};
            {SelectStaff} {filter} ORDER BY u.display_name, tu.id LIMIT @PageSize OFFSET @Offset;
            """,
            new { tenantId, status = query.Status, search, query.Page.PageSize, query.Page.Offset },
            cancellationToken);

        var total = await grid.ReadSingleAsync<long>();
        var rows = await grid.ReadAsync<StaffRow>();
        return new PagedResult<StaffMemberDto>(rows.Select(Map).ToList(), query.Page.Page, query.Page.PageSize, total);
    }

    public async Task<StaffMemberDto?> GetAsync(Guid tenantId, Guid tenantUserId, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<StaffRow>(
            $"{SelectStaff} WHERE tu.tenant_id = @tenantId AND tu.id = @tenantUserId",
            new { tenantId, tenantUserId },
            cancellationToken);
        return row is null ? null : Map(row);
    }

    public Task<Guid?> FindMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM tenant_users WHERE tenant_id = @tenantId AND user_id = @userId",
            new { tenantId, userId },
            cancellationToken);

    public Task<Guid> CreateMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<Guid>(
            "INSERT INTO tenant_users (tenant_id, user_id) VALUES (@tenantId, @userId) RETURNING id",
            new { tenantId, userId },
            cancellationToken);

    public async Task ReplaceRolesAsync(Guid tenantId, Guid tenantUserId, IReadOnlyCollection<string> roleCodes, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync(
            "DELETE FROM tenant_user_roles WHERE tenant_id = @tenantId AND tenant_user_id = @tenantUserId",
            new { tenantId, tenantUserId },
            cancellationToken);

        var inserted = await db.ExecuteAsync(
            """
            INSERT INTO tenant_user_roles (tenant_id, tenant_user_id, role_id)
            SELECT @tenantId, @tenantUserId, r.id FROM roles r
            WHERE r.tenant_id IS NULL AND r.code = ANY(@roleCodes)
            """,
            new { tenantId, tenantUserId, roleCodes = roleCodes.ToArray() },
            cancellationToken);

        if (inserted != roleCodes.Count)
        {
            throw new InvalidOperationException("One or more role codes do not exist.");
        }
    }

    public Task SetStatusAsync(Guid tenantId, Guid tenantUserId, string status, CancellationToken cancellationToken) =>
        db.ExecuteAsync(
            "UPDATE tenant_users SET status = @status, updated_at = now() WHERE tenant_id = @tenantId AND id = @tenantUserId",
            new { tenantId, tenantUserId, status },
            cancellationToken);

    public Task<int> CountActiveOwnersAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<int>(
            """
            SELECT count(DISTINCT tu.id)::int
            FROM tenant_users tu
            JOIN tenant_user_roles tur ON tur.tenant_user_id = tu.id
            JOIN roles r ON r.id = tur.role_id
            JOIN users u ON u.id = tu.user_id
            WHERE tu.tenant_id = @tenantId AND tu.status = 'ACTIVE' AND u.status = 'ACTIVE' AND r.code = @owner
            """,
            new { tenantId, owner = Roles.TenantOwner },
            cancellationToken);

    internal static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);

    private static StaffMemberDto Map(StaffRow row) =>
        new(row.TenantUserId, row.UserId, row.Email, row.DisplayName, row.Status, row.Roles, row.VenueIds, row.CreatedAt);

    private sealed class StaffRow
    {
        public Guid TenantUserId { get; init; }
        public Guid UserId { get; init; }
        public string Email { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public string[] Roles { get; init; } = [];
        public Guid[] VenueIds { get; init; } = [];
    }
}

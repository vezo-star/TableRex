using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Identity;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Identity;

public sealed class MembershipRepository(DbSession db) : IMembershipRepository
{
    public async Task<IReadOnlyList<MembershipSummary>> GetActiveMembershipsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<MembershipRow>(
            """
            SELECT t.id AS tenant_id, t.name AS tenant_name, t.slug AS tenant_slug,
                   COALESCE(array_agg(r.code ORDER BY r.code) FILTER (WHERE r.code IS NOT NULL), '{}') AS roles
            FROM tenant_users tu
            JOIN tenants t ON t.id = tu.tenant_id
            LEFT JOIN tenant_user_roles tur ON tur.tenant_user_id = tu.id
            LEFT JOIN roles r ON r.id = tur.role_id
            WHERE tu.user_id = @userId AND tu.status = 'ACTIVE' AND t.status = 'ACTIVE'
            GROUP BY t.id, t.name, t.slug
            ORDER BY t.name
            """,
            new { userId },
            cancellationToken);

        return rows.Select(r => new MembershipSummary(r.TenantId, r.TenantName, r.TenantSlug, r.Roles)).ToList();
    }

    public async Task<TenantAccess?> GetTenantAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var row = await db.QuerySingleOrDefaultAsync<AccessRow>(
            """
            SELECT tu.id AS tenant_user_id,
                   COALESCE(array_agg(DISTINCT r.code) FILTER (WHERE r.code IS NOT NULL), '{}') AS roles,
                   COALESCE(array_agg(DISTINCT rp.permission_code) FILTER (WHERE rp.permission_code IS NOT NULL), '{}') AS permissions
            FROM tenant_users tu
            JOIN tenants t ON t.id = tu.tenant_id
            JOIN users u ON u.id = tu.user_id
            LEFT JOIN tenant_user_roles tur ON tur.tenant_user_id = tu.id
            LEFT JOIN roles r ON r.id = tur.role_id
            LEFT JOIN role_permissions rp ON rp.role_id = r.id
            WHERE tu.user_id = @userId AND tu.tenant_id = @tenantId
              AND tu.status = 'ACTIVE' AND t.status = 'ACTIVE' AND u.status = 'ACTIVE'
            GROUP BY tu.id
            """,
            new { userId, tenantId },
            cancellationToken);

        return row is null
            ? null
            : new TenantAccess(tenantId, row.TenantUserId, new HashSet<string>(row.Roles, StringComparer.Ordinal),
                new HashSet<string>(row.Permissions, StringComparer.Ordinal));
    }

    private sealed class MembershipRow
    {
        public Guid TenantId { get; init; }
        public string TenantName { get; init; } = string.Empty;
        public string TenantSlug { get; init; } = string.Empty;
        public string[] Roles { get; init; } = [];
    }

    private sealed class AccessRow
    {
        public Guid TenantUserId { get; init; }
        public string[] Roles { get; init; } = [];
        public string[] Permissions { get; init; } = [];
    }
}

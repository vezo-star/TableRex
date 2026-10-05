using BookingPlatform.Application.Tenancy;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Tenancy;

public sealed class RoleRepository(DbSession db) : IRoleRepository
{
    private static readonly string[] Order = ["TENANT_OWNER", "TENANT_ADMIN", "MANAGER", "HOST", "STAFF", "VIEW_ONLY"];

    public async Task<IReadOnlyList<RoleDto>> ListSystemRolesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.QueryAsync<RoleRow>(
            """
            SELECT r.code, r.name,
                   COALESCE(array_agg(rp.permission_code ORDER BY rp.permission_code) FILTER (WHERE rp.permission_code IS NOT NULL), '{}') AS permissions
            FROM roles r
            LEFT JOIN role_permissions rp ON rp.role_id = r.id
            WHERE r.tenant_id IS NULL
            GROUP BY r.code, r.name
            """,
            null,
            cancellationToken);

        return rows
            .OrderBy(r => Array.IndexOf(Order, r.Code) is var i && i < 0 ? int.MaxValue : i)
            .Select(r => new RoleDto(r.Code, r.Name, r.Permissions))
            .ToList();
    }

    private sealed class RoleRow
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string[] Permissions { get; init; } = [];
    }
}

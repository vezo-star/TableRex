using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Security;

namespace BookingPlatform.Application.Tenancy;

public sealed record TenantDto(Guid Id, string Name, string Slug, string Status, string DefaultTimeZone, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record UpdateTenantRequest(string Name, string DefaultTimeZone);

public sealed record CreateTenantRequest(string Name, string Slug, string DefaultTimeZone, NewOwnerRequest Owner);

public sealed record NewOwnerRequest(string Email, string DisplayName, string? Password);

public sealed record StaffMemberDto(
    Guid TenantUserId,
    Guid UserId,
    string Email,
    string DisplayName,
    string Status,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> VenueIds,
    DateTime CreatedAt);

public sealed record CreateStaffRequest(string Email, string DisplayName, string? Password, IReadOnlyList<string> Roles);

public sealed record CreateStaffResult(StaffMemberDto Staff, bool AccountCreated);

public sealed record UpdateStaffRolesRequest(IReadOnlyList<string> Roles);

public sealed record RoleDto(string Code, string Name, IReadOnlyList<string> Permissions);

public sealed record StaffQuery(string? Status, string? Search, PageRequest Page);

public interface ITenantRepository
{
    Task<TenantDto?> GetAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Locks the tenant row; used to serialise owner changes (last-owner rule).</summary>
    Task LockAsync(Guid tenantId, CancellationToken cancellationToken);

    Task UpdateAsync(Guid tenantId, string name, string defaultTimeZone, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(string name, string slug, string defaultTimeZone, CancellationToken cancellationToken);

    Task<PagedResult<TenantDto>> ListAsync(PageRequest page, CancellationToken cancellationToken);
}

public interface IStaffRepository
{
    Task<PagedResult<StaffMemberDto>> ListAsync(Guid tenantId, StaffQuery query, CancellationToken cancellationToken);

    Task<StaffMemberDto?> GetAsync(Guid tenantId, Guid tenantUserId, CancellationToken cancellationToken);

    Task<Guid?> FindMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<Guid> CreateMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task ReplaceRolesAsync(Guid tenantId, Guid tenantUserId, IReadOnlyCollection<string> roleCodes, CancellationToken cancellationToken);

    Task SetStatusAsync(Guid tenantId, Guid tenantUserId, string status, CancellationToken cancellationToken);

    Task<int> CountActiveOwnersAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface IRoleRepository
{
    Task<IReadOnlyList<RoleDto>> ListSystemRolesAsync(CancellationToken cancellationToken);
}

/// <summary>Per-instance cache of resolved tenant access (≤60 s, SECURITY.md §2). Writers invalidate on change.</summary>
public interface ITenantAccessCache
{
    Task<TenantAccess?> GetAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);

    Task<UserAccessFlags?> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    void Invalidate(Guid userId, Guid tenantId);

    void InvalidateUser(Guid userId);
}

public sealed record UserAccessFlags(string Status, bool IsPlatformAdmin);

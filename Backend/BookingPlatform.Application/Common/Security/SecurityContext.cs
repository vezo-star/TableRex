using BookingPlatform.Application.Common.Errors;

namespace BookingPlatform.Application.Common.Security;

/// <summary>The authenticated principal of the current request (from the validated access token).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    /// <summary>The <c>tid</c> claim; only trusted after <see cref="ITenantContext"/> verified the membership.</summary>
    Guid? TokenTenantId { get; }

    bool IsPlatformAdmin { get; }

    Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedAppException(ErrorCodes.Unauthorized, "Authentication is required.");
}

/// <summary>Verified tenant membership for the current request. Every tenant-owned query is scoped by <see cref="TenantId"/>.</summary>
public interface ITenantContext
{
    bool HasTenant { get; }

    Guid TenantId { get; }

    Guid TenantUserId { get; }

    IReadOnlySet<string> Roles { get; }

    IReadOnlySet<string> Permissions { get; }

    /// <summary>True for TENANT_OWNER / TENANT_ADMIN (D-018).</summary>
    bool HasAllVenueAccess { get; }

    bool HasPermission(string permission) => HasTenant && Permissions.Contains(permission);
}

/// <summary>Request metadata for audit and security logs. Values are already sanitised and length-limited.</summary>
public interface IRequestMetadata
{
    string? IpAddress { get; }

    string? UserAgent { get; }

    string? CorrelationId { get; }
}

/// <summary>Resolved access of an authenticated user to one tenant.</summary>
public sealed record TenantAccess(
    Guid TenantId,
    Guid TenantUserId,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions);

/// <summary>Mutable holder populated once per request by the tenant context middleware.</summary>
public sealed class RequestTenantContext : ITenantContext
{
    private TenantAccess? _access;

    public bool HasTenant => _access is not null;

    public Guid TenantId => Require().TenantId;

    public Guid TenantUserId => Require().TenantUserId;

    public IReadOnlySet<string> Roles => _access?.Roles ?? EmptySet;

    public IReadOnlySet<string> Permissions => _access?.Permissions ?? EmptySet;

    public bool HasAllVenueAccess => _access is not null && Security.Roles.GrantsAllVenues(_access.Roles);

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    public void Set(TenantAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (_access is not null && _access.TenantId != access.TenantId)
        {
            throw new InvalidOperationException("The tenant context is already set for this scope.");
        }

        _access = access;
    }

    private TenantAccess Require() =>
        _access ?? throw new ForbiddenAppException("Select a tenant before accessing tenant data.");
}

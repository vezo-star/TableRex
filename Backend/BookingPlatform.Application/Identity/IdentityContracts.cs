namespace BookingPlatform.Application.Identity;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int RefreshTokenDays { get; set; } = 14;

    public int RefreshTokenAbsoluteDays { get; set; } = 30;

    public int LockoutThreshold { get; set; } = 5;

    public int LockoutBaseMinutes { get; set; } = 1;

    public int LockoutMaxMinutes { get; set; } = 60;
}

public enum PasswordVerification
{
    Failed,
    Success,
    SuccessRehashNeeded,
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerification Verify(string hashedPassword, string providedPassword);

    /// <summary>Burns comparable CPU time when the user does not exist, so timing doesn't reveal accounts.</summary>
    void VerifyDummy(string providedPassword);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public interface IAccessTokenIssuer
{
    AccessToken Issue(Guid userId, Guid? tenantId);
}

public sealed record UserRecord
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsPlatformAdmin { get; init; }
    public int FailedLoginCount { get; init; }
    public DateTime? LockoutUntil { get; init; }
    public DateTime? LastLoginAt { get; init; }
}

public sealed record RefreshTokenRecord
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid FamilyId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime FamilyExpiresAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public Guid? ReplacedById { get; init; }
}

public sealed record NewRefreshToken(
    Guid UserId,
    Guid? TenantId,
    byte[] TokenHash,
    Guid FamilyId,
    DateTime ExpiresAt,
    DateTime FamilyExpiresAt,
    string? CreatedIp,
    string? UserAgent);

public sealed record MembershipSummary(Guid TenantId, string TenantName, string TenantSlug, IReadOnlyList<string> Roles);

public interface IUserRepository
{
    Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    Task<UserRecord?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Locks the user row for the rest of the transaction (serialises concurrent login attempts).</summary>
    Task<UserRecord?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken);

    Task RecordFailedLoginAsync(Guid userId, int failedCount, DateTime? lockoutUntil, CancellationToken cancellationToken);

    Task RecordSuccessfulLoginAsync(Guid userId, DateTime now, string? rehashedPassword, CancellationToken cancellationToken);

    Task UpdatePasswordAsync(Guid userId, string passwordHash, DateTime now, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(string email, string passwordHash, string displayName, bool isPlatformAdmin, CancellationToken cancellationToken);

    /// <summary>Lightweight per-request check: status and platform flag.</summary>
    Task<(string Status, bool IsPlatformAdmin)?> GetAccessFlagsAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IRefreshTokenRepository
{
    Task<Guid> InsertAsync(NewRefreshToken token, CancellationToken cancellationToken);

    /// <summary>Locks the row (FOR UPDATE) so concurrent refreshes of the same token serialise.</summary>
    Task<RefreshTokenRecord?> GetByHashForUpdateAsync(byte[] tokenHash, CancellationToken cancellationToken);

    Task MarkRotatedAsync(Guid id, Guid replacedById, DateTime now, CancellationToken cancellationToken);

    Task RevokeFamilyAsync(Guid familyId, string reason, DateTime now, CancellationToken cancellationToken);

    Task RevokeAllForUserAsync(Guid userId, string reason, DateTime now, CancellationToken cancellationToken);

    Task RevokeAllForUserInTenantAsync(Guid userId, Guid tenantId, string reason, DateTime now, CancellationToken cancellationToken);

    Task<int> PurgeExpiredAsync(DateTime olderThan, CancellationToken cancellationToken);
}

public interface IMembershipRepository
{
    /// <summary>Active memberships in active tenants.</summary>
    Task<IReadOnlyList<MembershipSummary>> GetActiveMembershipsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Roles and permissions of an active membership in an active tenant for an active user; null otherwise.</summary>
    Task<Common.Security.TenantAccess?> GetTenantAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
}

public sealed record PasswordResetRequestRecord
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? UsedAt { get; init; }
    public DateTime? InvalidatedAt { get; init; }
}

public interface IPasswordResetRepository
{
    Task InsertAsync(Guid id, Guid userId, DateTime expiresAt, string? requestedIp, CancellationToken cancellationToken);

    /// <summary>Earlier unused requests stop working once a newer one is made or the password changes.</summary>
    Task InvalidateOutstandingAsync(Guid userId, DateTime now, CancellationToken cancellationToken);

    Task<PasswordResetRequestRecord?> GetByTokenHashForUpdateAsync(byte[] tokenHash, CancellationToken cancellationToken);

    Task MarkUsedAsync(Guid id, DateTime now, CancellationToken cancellationToken);

    Task<Notifications.PasswordResetDelivery?> GetForDeliveryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Stores the hash of a freshly generated token for a request that is still open; false when it no longer is.</summary>
    Task<bool> SetTokenHashAsync(Guid id, byte[] tokenHash, DateTime now, CancellationToken cancellationToken);
}

public static class RevocationReasons
{
    public const string Rotated = "ROTATED";
    public const string Reuse = "REUSE_DETECTED";
    public const string Logout = "LOGOUT";
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string PasswordReset = "PASSWORD_RESET";
    public const string TenantSwitched = "TENANT_SWITCHED";
    public const string MembershipRevoked = "MEMBERSHIP_REVOKED";
    public const string UserDisabled = "USER_DISABLED";
}

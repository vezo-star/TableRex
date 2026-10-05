namespace BookingPlatform.Application.Common.Auditing;

/// <summary>
/// One audit row. Values are serialised to JSON; callers pass DTO-shaped objects that never contain
/// password hashes, tokens or other secrets (SECURITY.md §8).
/// </summary>
public sealed record AuditEntry(
    Guid? TenantId,
    string EntityType,
    Guid? EntityId,
    string Action,
    object? OldValues = null,
    object? NewValues = null)
{
    /// <summary>Overrides the actor when the request is not yet authenticated (for example during login).</summary>
    public Guid? ActorUserId { get; init; }
}

public interface IAuditWriter
{
    /// <summary>Writes inside the current unit of work when one is open; user, IP, user agent and correlation id are added automatically.</summary>
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);
}

public static class AuditActions
{
    public const string LoginSucceeded = "auth.login.succeeded";
    public const string LoginFailed = "auth.login.failed";
    public const string AccountLocked = "auth.account.locked";
    public const string Logout = "auth.logout";
    public const string TenantSwitched = "auth.tenant.switched";
    public const string RefreshReuseDetected = "auth.refresh.reuse_detected";
    public const string PasswordChanged = "auth.password.changed";
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deactivated = "deactivated";
    public const string Deleted = "deleted";
    public const string RolesChanged = "roles.changed";
    public const string VenuesChanged = "venues.changed";
    public const string StatusChanged = "status.changed";
    public const string Moved = "moved";
    public const string Cancelled = "cancelled";
    public const string Anonymized = "anonymized";
}

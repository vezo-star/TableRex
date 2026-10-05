namespace BookingPlatform.Domain.Identity;

public static class UserStatus
{
    public const string Active = "ACTIVE";
    public const string Locked = "LOCKED";
    public const string Disabled = "DISABLED";
}

public static class TenantStatus
{
    public const string Active = "ACTIVE";
    public const string Suspended = "SUSPENDED";
    public const string Archived = "ARCHIVED";
}

public static class MembershipStatus
{
    public const string Active = "ACTIVE";
    public const string Invited = "INVITED";
    public const string Disabled = "DISABLED";
}

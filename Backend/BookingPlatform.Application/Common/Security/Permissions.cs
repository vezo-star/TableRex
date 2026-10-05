namespace BookingPlatform.Application.Common.Security;

/// <summary>Permission codes seeded by V027. Never rename a value; add new ones with a migration.</summary>
public static class Permissions
{
    public const string BookingView = "booking.view";
    public const string BookingCreate = "booking.create";
    public const string BookingUpdate = "booking.update";
    public const string BookingCancel = "booking.cancel";
    public const string BookingMove = "booking.move";
    public const string BookingCheckIn = "booking.checkin";
    public const string BookingSeat = "booking.seat";
    public const string ResourceView = "resource.view";
    public const string ResourceCreate = "resource.create";
    public const string ResourceUpdate = "resource.update";
    public const string ResourceDelete = "resource.delete";
    public const string VenueView = "venue.view";
    public const string VenueManage = "venue.manage";
    public const string FloorPlanView = "floorplan.view";
    public const string FloorPlanManage = "floorplan.manage";
    public const string CustomerView = "customer.view";
    public const string CustomerManage = "customer.manage";
    public const string StaffView = "staff.view";
    public const string StaffManage = "staff.manage";
    public const string ReportsView = "reports.view";
    public const string SettingsManage = "settings.manage";
    public const string PaymentView = "payment.view";
    public const string PaymentRefund = "payment.refund";
}

/// <summary>System role codes seeded by V027.</summary>
public static class Roles
{
    public const string TenantOwner = "TENANT_OWNER";
    public const string TenantAdmin = "TENANT_ADMIN";
    public const string Manager = "MANAGER";
    public const string Host = "HOST";
    public const string Staff = "STAFF";
    public const string ViewOnly = "VIEW_ONLY";

    public static readonly IReadOnlyList<string> All = [TenantOwner, TenantAdmin, Manager, Host, Staff, ViewOnly];

    /// <summary>Roles that implicitly access every venue of the tenant (D-018).</summary>
    public static bool GrantsAllVenues(IEnumerable<string> roles) =>
        roles.Any(role => role is TenantOwner or TenantAdmin);
}

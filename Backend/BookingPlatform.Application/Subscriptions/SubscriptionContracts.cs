namespace BookingPlatform.Application.Subscriptions;

/// <summary>A plan's capabilities. A null limit means unlimited. Plans carry no prices: billing is out of scope (D-088).</summary>
public sealed record PlanDto(
    string Code,
    string Name,
    string? Description,
    int? MaxVenues,
    int? MaxActiveStaff,
    int? MaxResourcesPerVenue,
    bool CustomBranding);

public sealed record PlanLimitsDto(int? MaxVenues, int? MaxActiveStaff, int? MaxResourcesPerVenue);

/// <param name="MaxResourcesInAVenue">Active resources in the tenant's fullest venue (the per-venue limit applies to each venue).</param>
/// <param name="BookingsThisMonth">Non-cancelled bookings dated in the current month (tenant time zone).</param>
public sealed record SubscriptionUsageDto(int Venues, int ActiveStaff, int MaxResourcesInAVenue, int BookingsThisMonth);

/// <param name="Limits">Effective limits: a per-tenant override when set, otherwise the plan's limit.</param>
/// <param name="Overrides">Per-tenant overrides only (null = use the plan's limit).</param>
public sealed record TenantSubscriptionDto(
    PlanDto Plan,
    PlanLimitsDto Limits,
    PlanLimitsDto Overrides,
    SubscriptionUsageDto Usage,
    DateTime? UpdatedAt);

/// <summary>Platform admin: assigns a plan and optional per-tenant limit overrides (null = use the plan's limit).</summary>
public sealed record UpdateSubscriptionRequest(
    string PlanCode,
    int? MaxVenuesOverride = null,
    int? MaxActiveStaffOverride = null,
    int? MaxResourcesPerVenueOverride = null);

/// <param name="Enabled">Whether the tenant's plan includes custom branding; when false the values are kept but not shown publicly.</param>
public sealed record BrandingDto(string? PrimaryColor, string? LogoUrl, string? BookingPageIntro, string? BookingTerms, bool Enabled, DateTime? UpdatedAt);

public sealed record UpdateBrandingRequest(string? PrimaryColor, string? LogoUrl, string? BookingPageIntro, string? BookingTerms);

/// <summary>Branding shown on public booking pages. Plain text, a validated colour and an https image URL; never HTML.</summary>
public sealed record PublicBrandingDto(string? PrimaryColor, string? LogoUrl, string? BookingPageIntro, string? BookingTerms);

public sealed record BrandingValues(string? PrimaryColor, string? LogoUrl, string? BookingPageIntro, string? BookingTerms)
{
    public bool IsEmpty => PrimaryColor is null && LogoUrl is null && BookingPageIntro is null && BookingTerms is null;
}

public sealed class BrandingRecord
{
    public string? PrimaryColor { get; init; }
    public string? LogoUrl { get; init; }
    public string? BookingPageIntro { get; init; }
    public string? BookingTerms { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>A tenant's plan joined with its overrides. Tenants without a subscription row get the default plan.</summary>
public sealed class TenantPlanRecord
{
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public string? PlanDescription { get; init; }
    public int? PlanMaxVenues { get; init; }
    public int? PlanMaxActiveStaff { get; init; }
    public int? PlanMaxResourcesPerVenue { get; init; }
    public bool CustomBranding { get; init; }
    public int? MaxVenuesOverride { get; init; }
    public int? MaxActiveStaffOverride { get; init; }
    public int? MaxResourcesPerVenueOverride { get; init; }
    public DateTime? UpdatedAt { get; init; }

    public int? MaxVenues => MaxVenuesOverride ?? PlanMaxVenues;
    public int? MaxActiveStaff => MaxActiveStaffOverride ?? PlanMaxActiveStaff;
    public int? MaxResourcesPerVenue => MaxResourcesPerVenueOverride ?? PlanMaxResourcesPerVenue;
}

public interface ISubscriptionRepository
{
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    Task<bool> PlanExistsAsync(string planCode, CancellationToken cancellationToken);

    Task<TenantPlanRecord> GetAsync(Guid tenantId, CancellationToken cancellationToken);

    Task UpsertAsync(Guid tenantId, UpdateSubscriptionRequest request, DateTime now, CancellationToken cancellationToken);

    Task<SubscriptionUsageDto> GetUsageAsync(Guid tenantId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken);

    Task<int> CountVenuesAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<int> CountActiveStaffAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<int> CountActiveResourcesAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken);

    Task<BrandingRecord?> GetBrandingAsync(Guid tenantId, CancellationToken cancellationToken);

    Task UpsertBrandingAsync(Guid tenantId, BrandingValues branding, DateTime now, CancellationToken cancellationToken);
}

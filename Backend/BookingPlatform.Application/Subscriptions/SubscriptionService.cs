using BookingPlatform.Application.Common.Auditing;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Persistence;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Tenancy;
using BookingPlatform.Domain.Time;
using FluentValidation;
using NodaTime;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.Subscriptions;

/// <summary>
/// Plans, per-tenant limits, usage and booking-page branding. This module only reads booking data for usage counts;
/// the booking engine never consults it (D-088).
/// </summary>
public sealed class SubscriptionService(
    ISubscriptionRepository subscriptions,
    ITenantRepository tenants,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IClock clock,
    IValidator<UpdateSubscriptionRequest> subscriptionValidator,
    IValidator<UpdateBrandingRequest> brandingValidator)
{
    private const string TenantNotFoundMessage = "The tenant was not found.";

    public Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
        subscriptions.ListPlansAsync(cancellationToken);

    public Task<TenantSubscriptionDto> GetCurrentAsync(CancellationToken cancellationToken) =>
        GetForTenantAsync(tenantContext.TenantId, cancellationToken);

    /// <summary>Platform admin (or the current tenant): plan, effective limits and current usage.</summary>
    public async Task<TenantSubscriptionDto> GetForTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetAsync(tenantId, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.TenantNotFound, TenantNotFoundMessage);
        var plan = await subscriptions.GetAsync(tenantId, cancellationToken);
        var today = Instant.FromDateTimeUtc(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc)).InZone(TimeZones.Get(tenant.DefaultTimeZone)).Date.ToDateOnly();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var usage = await subscriptions.GetUsageAsync(tenantId, monthStart, monthStart.AddMonths(1).AddDays(-1), cancellationToken);
        return ToDto(plan, usage);
    }

    /// <summary>Platform admin: assigns a plan and overrides. Lowering a limit never removes data; it only blocks additions.</summary>
    public async Task<TenantSubscriptionDto> UpdateForTenantAsync(Guid tenantId, UpdateSubscriptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await subscriptionValidator.EnsureValidAsync(request, cancellationToken);

        await unitOfWork.ExecuteAsync(async () =>
        {
            if (await tenants.GetAsync(tenantId, cancellationToken) is null)
            {
                throw new NotFoundAppException(ErrorCodes.TenantNotFound, TenantNotFoundMessage);
            }

            await tenants.LockAsync(tenantId, cancellationToken);
            if (!await subscriptions.PlanExistsAsync(request.PlanCode, cancellationToken))
            {
                throw new ValidationAppException("planCode", "Unknown plan.");
            }

            var before = await subscriptions.GetAsync(tenantId, cancellationToken);
            await subscriptions.UpsertAsync(tenantId, request, clock.UtcNow, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "tenant_subscription", tenantId, AuditActions.Updated,
                new { before.PlanCode, before.MaxVenuesOverride, before.MaxActiveStaffOverride, before.MaxResourcesPerVenueOverride },
                new { request.PlanCode, request.MaxVenuesOverride, request.MaxActiveStaffOverride, request.MaxResourcesPerVenueOverride }),
                cancellationToken);
        }, cancellationToken);

        return await GetForTenantAsync(tenantId, cancellationToken);
    }

    public async Task<BrandingDto> GetBrandingAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        var plan = await subscriptions.GetAsync(tenantId, cancellationToken);
        var branding = await subscriptions.GetBrandingAsync(tenantId, cancellationToken);
        return new BrandingDto(branding?.PrimaryColor, branding?.LogoUrl, branding?.BookingPageIntro, branding?.BookingTerms,
            plan.CustomBranding, branding?.UpdatedAt);
    }

    public async Task<BrandingDto> UpdateBrandingAsync(UpdateBrandingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await brandingValidator.EnsureValidAsync(request, cancellationToken);
        var tenantId = tenantContext.TenantId;
        var values = new BrandingValues(
            Clean(request.PrimaryColor)?.ToLowerInvariant(),
            Clean(request.LogoUrl),
            Clean(request.BookingPageIntro),
            Clean(request.BookingTerms));

        await unitOfWork.ExecuteAsync(async () =>
        {
            await tenants.LockAsync(tenantId, cancellationToken);
            var plan = await subscriptions.GetAsync(tenantId, cancellationToken);
            if (!plan.CustomBranding)
            {
                throw new BusinessRuleAppException(ErrorCodes.PlanFeatureUnavailable,
                    $"Custom branding is not included in the {plan.PlanName} plan. Upgrade the plan to brand your booking pages.");
            }

            var before = await subscriptions.GetBrandingAsync(tenantId, cancellationToken);
            await subscriptions.UpsertBrandingAsync(tenantId, values, clock.UtcNow, cancellationToken);
            await audit.WriteAsync(new AuditEntry(tenantId, "tenant_branding", tenantId, AuditActions.Updated,
                before is null ? null : new { before.PrimaryColor, before.LogoUrl, before.BookingPageIntro, before.BookingTerms }, values),
                cancellationToken);
        }, cancellationToken);

        return await GetBrandingAsync(cancellationToken);
    }

    /// <summary>Branding for public pages, or null when the plan does not include it or nothing is set.</summary>
    internal async Task<PublicBrandingDto?> GetPublicBrandingAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await subscriptions.GetAsync(tenantId, cancellationToken);
        if (!plan.CustomBranding)
        {
            return null;
        }

        var branding = await subscriptions.GetBrandingAsync(tenantId, cancellationToken);
        if (branding is null)
        {
            return null;
        }

        var values = new BrandingValues(branding.PrimaryColor, branding.LogoUrl, branding.BookingPageIntro, branding.BookingTerms);
        return values.IsEmpty ? null : new PublicBrandingDto(values.PrimaryColor, values.LogoUrl, values.BookingPageIntro, values.BookingTerms);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TenantSubscriptionDto ToDto(TenantPlanRecord plan, SubscriptionUsageDto usage) => new(
        new PlanDto(plan.PlanCode, plan.PlanName, plan.PlanDescription, plan.PlanMaxVenues, plan.PlanMaxActiveStaff, plan.PlanMaxResourcesPerVenue, plan.CustomBranding),
        new PlanLimitsDto(plan.MaxVenues, plan.MaxActiveStaff, plan.MaxResourcesPerVenue),
        new PlanLimitsDto(plan.MaxVenuesOverride, plan.MaxActiveStaffOverride, plan.MaxResourcesPerVenueOverride),
        usage,
        plan.UpdatedAt);
}

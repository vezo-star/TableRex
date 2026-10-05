using BookingPlatform.Application.Subscriptions;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Subscriptions;

public sealed class SubscriptionRepository(DbSession db) : ISubscriptionRepository
{
    public Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
        db.QueryAsync<PlanDto>(
            """
            SELECT code, name, description, max_venues, max_active_staff, max_resources_per_venue, custom_branding
            FROM plans ORDER BY sort_order, code
            """,
            null, cancellationToken);

    public async Task<bool> PlanExistsAsync(string planCode, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM plans WHERE code = @planCode)", new { planCode }, cancellationToken);

    public Task<TenantPlanRecord> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<TenantPlanRecord>(
            """
            SELECT p.code AS plan_code, p.name AS plan_name, p.description AS plan_description,
                   p.max_venues AS plan_max_venues, p.max_active_staff AS plan_max_active_staff,
                   p.max_resources_per_venue AS plan_max_resources_per_venue, p.custom_branding,
                   s.max_venues_override, s.max_active_staff_override, s.max_resources_per_venue_override, s.updated_at
            FROM (SELECT @tenantId::uuid AS tenant_id) t
            LEFT JOIN tenant_subscriptions s ON s.tenant_id = t.tenant_id
            JOIN plans p ON p.code = coalesce(s.plan_code, (SELECT code FROM plans WHERE is_default))
            """,
            new { tenantId }, cancellationToken);

    public Task UpsertAsync(Guid tenantId, UpdateSubscriptionRequest request, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return db.ExecuteAsync(
            """
            INSERT INTO tenant_subscriptions (tenant_id, plan_code, max_venues_override, max_active_staff_override,
                                              max_resources_per_venue_override, created_at, updated_at)
            VALUES (@tenantId, @PlanCode, @MaxVenuesOverride, @MaxActiveStaffOverride, @MaxResourcesPerVenueOverride, @now, @now)
            ON CONFLICT (tenant_id) DO UPDATE SET
                plan_code = EXCLUDED.plan_code,
                max_venues_override = EXCLUDED.max_venues_override,
                max_active_staff_override = EXCLUDED.max_active_staff_override,
                max_resources_per_venue_override = EXCLUDED.max_resources_per_venue_override,
                updated_at = EXCLUDED.updated_at
            """,
            new { tenantId, request.PlanCode, request.MaxVenuesOverride, request.MaxActiveStaffOverride, request.MaxResourcesPerVenueOverride, now },
            cancellationToken);
    }

    public Task<SubscriptionUsageDto> GetUsageAsync(Guid tenantId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) =>
        db.QuerySingleAsync<SubscriptionUsageDto>(
            """
            SELECT
                (SELECT count(*) FROM venues WHERE tenant_id = @tenantId)::int AS venues,
                (SELECT count(*) FROM tenant_users WHERE tenant_id = @tenantId AND status = 'ACTIVE')::int AS active_staff,
                (SELECT coalesce(max(n), 0) FROM (
                    SELECT count(*) AS n FROM bookable_resources WHERE tenant_id = @tenantId AND is_active GROUP BY venue_id) r)::int
                    AS max_resources_in_a_venue,
                (SELECT count(*) FROM bookings
                 WHERE tenant_id = @tenantId AND business_date BETWEEN @monthStart AND @monthEnd AND status <> 'CANCELLED')::int
                    AS bookings_this_month
            """,
            new { tenantId, monthStart, monthEnd }, cancellationToken);

    public async Task<int> CountVenuesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM venues WHERE tenant_id = @tenantId", new { tenantId }, cancellationToken);

    public async Task<int> CountActiveStaffAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM tenant_users WHERE tenant_id = @tenantId AND status = 'ACTIVE'",
            new { tenantId }, cancellationToken);

    public async Task<int> CountActiveResourcesAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken) =>
        await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM bookable_resources WHERE tenant_id = @tenantId AND venue_id = @venueId AND is_active",
            new { tenantId, venueId }, cancellationToken);

    public Task<BrandingRecord?> GetBrandingAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.QuerySingleOrDefaultAsync<BrandingRecord>(
            "SELECT primary_color, logo_url, booking_page_intro, booking_terms, updated_at FROM tenant_branding WHERE tenant_id = @tenantId",
            new { tenantId }, cancellationToken);

    public Task UpsertBrandingAsync(Guid tenantId, BrandingValues branding, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(branding);
        return db.ExecuteAsync(
            """
            INSERT INTO tenant_branding (tenant_id, primary_color, logo_url, booking_page_intro, booking_terms, updated_at)
            VALUES (@tenantId, @PrimaryColor, @LogoUrl, @BookingPageIntro, @BookingTerms, @now)
            ON CONFLICT (tenant_id) DO UPDATE SET
                primary_color = EXCLUDED.primary_color, logo_url = EXCLUDED.logo_url,
                booking_page_intro = EXCLUDED.booking_page_intro, booking_terms = EXCLUDED.booking_terms, updated_at = EXCLUDED.updated_at
            """,
            new { tenantId, branding.PrimaryColor, branding.LogoUrl, branding.BookingPageIntro, branding.BookingTerms, now },
            cancellationToken);
    }
}

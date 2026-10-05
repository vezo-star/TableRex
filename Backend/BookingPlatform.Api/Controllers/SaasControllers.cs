using BookingPlatform.Api.Security;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Reports;
using BookingPlatform.Application.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/plans")]
[Produces("application/json")]
[Authorize]
public sealed class PlansController(SubscriptionService subscriptions) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PlanDto>> List(CancellationToken cancellationToken) => subscriptions.ListPlansAsync(cancellationToken);
}

/// <summary>The current tenant's plan, usage and booking-page branding.</summary>
[ApiController]
[Route("api/v1/tenants/current")]
[Produces("application/json")]
public sealed class TenantSubscriptionController(SubscriptionService subscriptions) : ControllerBase
{
    [HttpGet("subscription")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<TenantSubscriptionDto> Subscription(CancellationToken cancellationToken) => subscriptions.GetCurrentAsync(cancellationToken);

    [HttpGet("branding")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<BrandingDto> Branding(CancellationToken cancellationToken) => subscriptions.GetBrandingAsync(cancellationToken);

    [HttpPut("branding")]
    [HasPermission(Permissions.SettingsManage)]
    public Task<BrandingDto> UpdateBranding(UpdateBrandingRequest request, CancellationToken cancellationToken) =>
        subscriptions.UpdateBrandingAsync(request, cancellationToken);
}

/// <summary>Platform admin: tenant plans and limit overrides.</summary>
[ApiController]
[Route("api/v1/platform/tenants/{tenantId:guid}/subscription")]
[Produces("application/json")]
[RequirePlatformAdmin]
public sealed class PlatformSubscriptionsController(SubscriptionService subscriptions) : ControllerBase
{
    [HttpGet]
    public Task<TenantSubscriptionDto> Get(Guid tenantId, CancellationToken cancellationToken) =>
        subscriptions.GetForTenantAsync(tenantId, cancellationToken);

    [HttpPut]
    public Task<TenantSubscriptionDto> Update(Guid tenantId, UpdateSubscriptionRequest request, CancellationToken cancellationToken) =>
        subscriptions.UpdateForTenantAsync(tenantId, request, cancellationToken);
}

/// <summary>Booking analytics for a venue over an inclusive range of business dates (at most 366 days).</summary>
[ApiController]
[Route("api/v1/reports")]
[Produces("application/json")]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet("bookings-summary")]
    [HasPermission(Permissions.ReportsView)]
    public Task<BookingsSummaryDto> BookingsSummary(
        [FromQuery] Guid venueId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken) =>
        reports.GetBookingsSummaryAsync(new ReportQuery(venueId, from, to), cancellationToken);

    [HttpGet("bookings.csv")]
    [HasPermission(Permissions.ReportsView)]
    [Produces("text/csv")]
    public async Task<IActionResult> BookingsCsv(
        [FromQuery] Guid venueId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        var file = await reports.ExportBookingsCsvAsync(new ReportQuery(venueId, from, to), cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, "text/csv; charset=utf-8", file.FileName);
    }
}

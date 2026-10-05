using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Tenancy;

namespace BookingPlatform.Application.Subscriptions;

/// <summary>
/// Enforces the tenant's plan limits when venues, staff or resources are added (D-089). Every check must run inside
/// the caller's unit of work: it locks the tenant row first, so concurrent additions are counted one at a time and
/// cannot overshoot a limit. A downgrade never removes anything; it only blocks further additions.
/// </summary>
public sealed class PlanLimits(ISubscriptionRepository subscriptions, ITenantRepository tenants)
{
    public async Task EnsureCanAddVenueAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await LockAndGetPlanAsync(tenantId, cancellationToken);
        if (plan.MaxVenues is { } max && await subscriptions.CountVenuesAsync(tenantId, cancellationToken) >= max)
        {
            throw Reached(plan, max == 1 ? "1 venue" : $"{max} venues", "add another venue");
        }
    }

    public async Task EnsureCanAddStaffAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var plan = await LockAndGetPlanAsync(tenantId, cancellationToken);
        if (plan.MaxActiveStaff is { } max && await subscriptions.CountActiveStaffAsync(tenantId, cancellationToken) >= max)
        {
            throw Reached(plan, max == 1 ? "1 active staff member" : $"{max} active staff members", "add or reactivate staff");
        }
    }

    public async Task EnsureCanAddResourceAsync(Guid tenantId, Guid venueId, CancellationToken cancellationToken)
    {
        var plan = await LockAndGetPlanAsync(tenantId, cancellationToken);
        if (plan.MaxResourcesPerVenue is { } max && await subscriptions.CountActiveResourcesAsync(tenantId, venueId, cancellationToken) >= max)
        {
            throw Reached(plan, max == 1 ? "1 active resource per venue" : $"{max} active resources per venue", "add or reactivate resources in this venue");
        }
    }

    private async Task<TenantPlanRecord> LockAndGetPlanAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await tenants.LockAsync(tenantId, cancellationToken);
        return await subscriptions.GetAsync(tenantId, cancellationToken);
    }

    private static BusinessRuleAppException Reached(TenantPlanRecord plan, string allowance, string action) =>
        new(ErrorCodes.PlanLimitReached, $"Your {plan.PlanName} plan allows {allowance}. Upgrade the plan to {action}.");
}

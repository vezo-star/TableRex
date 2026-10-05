using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Security;

namespace BookingPlatform.Application.Venues;

public sealed class VenueAccessGuard(IVenueRepository venues, IVenueAccessRepository access, ITenantContext tenantContext) : IVenueAccessGuard
{
    public async Task<VenueRef> EnsureAccessAsync(Guid venueId, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        var venue = await venues.GetRefAsync(tenantId, venueId, cancellationToken);
        if (venue is null
            || (!tenantContext.HasAllVenueAccess
                && !await access.IsAssignedAsync(tenantId, tenantContext.TenantUserId, venueId, cancellationToken)))
        {
            throw new NotFoundAppException(ErrorCodes.VenueNotFound, "The venue was not found.");
        }

        return venue;
    }

    public async Task<IReadOnlyList<Guid>?> GetAccessibleVenueIdsAsync(CancellationToken cancellationToken) =>
        tenantContext.HasAllVenueAccess
            ? null
            : await access.ListAssignedAsync(tenantContext.TenantId, tenantContext.TenantUserId, cancellationToken);
}

using System.Security.Claims;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Tenancy;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Identity;
using BookingPlatform.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BookingPlatform.Api.Realtime;

public static class RealtimeGroups
{
    /// <summary>The tenant id always comes from the server-verified token, never from the client (REALTIME.md §2).</summary>
    public static string Venue(Guid tenantId, Guid venueId) => $"tenant:{tenantId:D}:venue:{venueId:D}";
}

/// <summary>
/// Staff connections subscribe to venue groups. Joining re-checks the user, the tenant membership, <c>booking.view</c>
/// and venue access (D-018); every failure looks the same so venue existence is never revealed.
/// </summary>
[Authorize]
public sealed class VenueHub(
    ITenantAccessCache accessCache,
    RequestTenantContext tenantContext,
    IVenueAccessGuard venueAccess,
    ILogger<VenueHub> logger) : Hub
{
    public const string Path = "/hubs/venue";
    public const string ForbiddenMessage = "FORBIDDEN";

    public async Task JoinVenue(Guid venueId)
    {
        var tenantId = await AuthorizeVenueAsync(venueId);
        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Venue(tenantId, venueId), Context.ConnectionAborted);
    }

    public async Task LeaveVenue(Guid venueId)
    {
        if (ParseClaim(Context.User, PlatformClaims.TenantId) is { } tenantId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Venue(tenantId, venueId), Context.ConnectionAborted);
        }
    }

    private async Task<Guid> AuthorizeVenueAsync(Guid venueId)
    {
        var cancellationToken = Context.ConnectionAborted;
        var userId = ParseClaim(Context.User, PlatformClaims.Subject);
        var tenantId = ParseClaim(Context.User, PlatformClaims.TenantId);
        if (userId is null || tenantId is null)
        {
            throw Deny(userId, venueId, "no tenant in token");
        }

        var flags = await accessCache.GetUserAsync(userId.Value, cancellationToken);
        if (flags is not { Status: UserStatus.Active })
        {
            throw Deny(userId, venueId, "user inactive");
        }

        var access = await accessCache.GetAsync(userId.Value, tenantId.Value, cancellationToken);
        if (access is null || !access.Permissions.Contains(Permissions.BookingView))
        {
            throw Deny(userId, venueId, "no membership or permission");
        }

        tenantContext.Set(access);
        try
        {
            await venueAccess.EnsureAccessAsync(venueId, cancellationToken);
        }
        catch (NotFoundAppException)
        {
            throw Deny(userId, venueId, "venue not accessible");
        }

        return tenantId.Value;
    }

    private HubException Deny(Guid? userId, Guid venueId, string reason)
    {
        logger.LogInformation("Realtime join denied for user {UserId} to venue {VenueId}: {Reason}", userId, venueId, reason);
        return new HubException(ForbiddenMessage);
    }

    private static Guid? ParseClaim(ClaimsPrincipal? user, string type) =>
        Guid.TryParse(user?.FindFirst(type)?.Value, out var value) ? value : null;
}

using BookingPlatform.Api.Security;
using BookingPlatform.Application.Payments;
using BookingPlatform.Application.PublicBooking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Controllers;

/// <summary>
/// Anonymous guest booking. Venues are addressed by tenant and venue slug; responses never contain internal ids,
/// resources, staff or customer records. The manage token travels in the <c>X-Booking-Token</c> header (never in a URL),
/// so it does not end up in request logs, proxies or browser history.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/public")]
[Produces("application/json")]
public sealed class PublicBookingController(PublicBookingService publicBooking) : ControllerBase
{
    public const string TokenHeader = "X-Booking-Token";

    [HttpGet("{tenantSlug}/{venueSlug}")]
    [RateLimit("public-read", 120, 60)]
    public Task<PublicVenueDto> Venue(string tenantSlug, string venueSlug, CancellationToken cancellationToken) =>
        publicBooking.GetVenueAsync(tenantSlug, venueSlug, cancellationToken);

    /// <summary>Bookable start times (venue-local) for a business date and party size.</summary>
    [HttpGet("{tenantSlug}/{venueSlug}/availability")]
    [RateLimit("public-read", 120, 60)]
    public Task<PublicAvailabilityDto> Availability(
        string tenantSlug, string venueSlug, [FromQuery] DateOnly date, [FromQuery] int partySize, CancellationToken cancellationToken) =>
        publicBooking.GetAvailabilityAsync(tenantSlug, venueSlug, date, partySize, cancellationToken);

    /// <summary>Creates a booking. Requires <c>Idempotency-Key</c>; a retry with the same key returns the same booking.</summary>
    [HttpPost("{tenantSlug}/{venueSlug}/bookings")]
    [RateLimit("public-booking", 10, 600)]
    [ProducesResponseType<PublicBookingConfirmationDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(string tenantSlug, string venueSlug, PublicBookingRequest request, CancellationToken cancellationToken)
    {
        var idempotency = this.CreateIdempotencyRequest($"/api/v1/public/{tenantSlug}/{venueSlug}/bookings", request);
        var result = await publicBooking.CreateAsync(tenantSlug, venueSlug, request, idempotency, cancellationToken);
        if (result.Replayed)
        {
            Response.Headers[IdempotencyExtensions.ReplayedHeader] = "true";
        }

        Response.Headers.CacheControl = "no-store";
        return StatusCode(StatusCodes.Status201Created, result.Booking);
    }

    /// <summary>The guest's booking, identified by the manage token in <c>X-Booking-Token</c>.</summary>
    [HttpGet("booking")]
    [RateLimit("public-manage", 60, 60)]
    public async Task<PublicBookingDto> Booking(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return await publicBooking.GetBookingAsync(Request.Headers[TokenHeader].ToString(), cancellationToken);
    }

    /// <summary>Cancels the guest's booking within the venue's online cancellation window.</summary>
    [HttpPost("booking/cancel")]
    [RateLimit("public-cancel", 10, 600)]
    public async Task<PublicBookingDto> Cancel(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return await publicBooking.CancelAsync(Request.Headers[TokenHeader].ToString(), cancellationToken);
    }

    /// <summary>Starts the deposit checkout for a HELD booking and returns the provider URL to send the guest to.</summary>
    [HttpPost("booking/payment")]
    [RateLimit("public-payment", 10, 600)]
    public async Task<PublicCheckoutDto> Pay(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return await publicBooking.StartPaymentAsync(Request.Headers[TokenHeader].ToString(), cancellationToken);
    }
}

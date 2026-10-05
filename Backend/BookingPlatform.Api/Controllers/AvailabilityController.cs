using BookingPlatform.Api.Security;
using BookingPlatform.Application.Availability;
using BookingPlatform.Application.Common.Security;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/availability")]
[Produces("application/json")]
public sealed class AvailabilityController(AvailabilityService availability) : ControllerBase
{
    /// <summary>
    /// Slots for a venue-local business date with ranked resource/combination options. <c>time</c> (HH:mm) narrows the
    /// result to slots within 90 minutes of the preferred start.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.BookingView)]
    public Task<AvailabilityResponse> Search(
        [FromQuery] Guid venueId,
        [FromQuery] DateOnly date,
        [FromQuery] int partySize,
        [FromQuery] int? durationMinutes,
        [FromQuery] TimeOnly? time,
        [FromQuery] Guid? sectionId,
        [FromQuery] Guid? resourceTypeId,
        CancellationToken cancellationToken) =>
        availability.SearchAsync(new AvailabilityRequest(venueId, date, partySize, durationMinutes, time, sectionId, resourceTypeId), cancellationToken);
}

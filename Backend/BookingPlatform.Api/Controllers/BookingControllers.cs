using BookingPlatform.Api.Security;
using BookingPlatform.Application.Bookings;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BookingPlatform.Api.Controllers;

[ApiController]
[Route("api/v1/bookings")]
[Produces("application/json")]
public sealed class BookingsController(BookingService bookings) : ControllerBase
{
    private const string CreateRoute = "/api/v1/bookings";

    /// <summary>Bookings visible to the caller. <c>status</c> may repeat or be comma-separated; <c>q</c> searches reference, name, email and phone.</summary>
    [HttpGet]
    [HasPermission(Permissions.BookingView)]
    public Task<PagedResult<BookingDto>> List(
        [FromQuery] Guid? venueId,
        [FromQuery] DateOnly? date,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string[]? status,
        [FromQuery] string? source,
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? resourceId,
        [FromQuery] Guid? sectionId,
        [FromQuery] string? q,
        [FromQuery] int page = PageRequest.DefaultPage,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var statuses = status?
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(s => s.ToUpperInvariant())
            .Distinct()
            .ToList();
        return bookings.ListAsync(
            new BookingListQuery(venueId, date, from, to, statuses is { Count: > 0 } ? statuses : null, source, customerId, resourceId, sectionId, q,
                new PageRequest(page, pageSize)),
            cancellationToken);
    }

    /// <summary>Creates a booking. Requires <c>Idempotency-Key</c>; a replay returns the original response with <c>Idempotent-Replayed: true</c>.</summary>
    [HttpPost]
    [HasPermission(Permissions.BookingCreate)]
    [ProducesResponseType<BookingDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var idempotency = this.CreateIdempotencyRequest(CreateRoute, request);
        var result = await bookings.CreateAsync(request, idempotency, cancellationToken);
        return this.IdempotentCreated(result, b => $"{CreateRoute}/{b.Id}");
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.BookingView)]
    public Task<BookingDto> Get(Guid id, CancellationToken cancellationToken) => bookings.GetAsync(id, cancellationToken);

    /// <summary>Updates party size, special requests, internal notes and customer; re-validates capacity.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.BookingUpdate)]
    public Task<BookingDto> Update(Guid id, UpdateBookingRequest request, CancellationToken cancellationToken) =>
        bookings.UpdateAsync(id, request with { Version = this.ResolveVersion(request.Version) }, cancellationToken);

    /// <summary>Moves the booking in time and/or to other resources atomically; on conflict the original allocation is kept.</summary>
    [HttpPost("{id:guid}/move")]
    [HasPermission(Permissions.BookingMove)]
    public Task<BookingDto> Move(Guid id, MoveBookingRequest request, CancellationToken cancellationToken) =>
        bookings.MoveAsync(id, request with { Version = this.ResolveVersion(request.Version) }, cancellationToken);

    [HttpPost("{id:guid}/confirm")]
    [HasPermission(Permissions.BookingUpdate)]
    public Task<BookingDto> Confirm(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingCommandRequest? request, CancellationToken cancellationToken) =>
        bookings.ConfirmAsync(id, Command(request), cancellationToken);

    [HttpPost("{id:guid}/check-in")]
    [HasPermission(Permissions.BookingCheckIn)]
    public Task<BookingDto> CheckIn(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingCommandRequest? request, CancellationToken cancellationToken) =>
        bookings.CheckInAsync(id, Command(request), cancellationToken);

    [HttpPost("{id:guid}/seat")]
    [HasPermission(Permissions.BookingSeat)]
    public Task<BookingDto> Seat(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingCommandRequest? request, CancellationToken cancellationToken) =>
        bookings.SeatAsync(id, Command(request), cancellationToken);

    /// <summary>Completes a seated booking; if before the planned end, the resource is released early (after turnover).</summary>
    [HttpPost("{id:guid}/complete")]
    [HasPermission(Permissions.BookingSeat)]
    public Task<BookingDto> Complete(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingCommandRequest? request, CancellationToken cancellationToken) =>
        bookings.CompleteAsync(id, Command(request), cancellationToken);

    [HttpPost("{id:guid}/no-show")]
    [HasPermission(Permissions.BookingUpdate)]
    public Task<BookingDto> NoShow(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingCommandRequest? request, CancellationToken cancellationToken) =>
        bookings.NoShowAsync(id, Command(request), cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.BookingCancel)]
    public Task<BookingDto> Cancel(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelBookingRequest? request, CancellationToken cancellationToken)
    {
        request ??= new CancelBookingRequest();
        return bookings.CancelAsync(id, request with { Version = this.ResolveVersion(request.Version) }, cancellationToken);
    }

    /// <summary>Status history and the business event timeline.</summary>
    [HttpGet("{id:guid}/history")]
    [HasPermission(Permissions.BookingView)]
    public Task<BookingHistoryDto> History(Guid id, CancellationToken cancellationToken) => bookings.GetHistoryAsync(id, cancellationToken);

    private BookingCommandRequest Command(BookingCommandRequest? request) => new(this.ResolveVersion(request?.Version));
}

[ApiController]
[Route("api/v1/holds")]
[Produces("application/json")]
public sealed class HoldsController(HoldService holds) : ControllerBase
{
    private const string CreateRoute = "/api/v1/holds";

    /// <summary>Creates a short hold. Requires <c>Idempotency-Key</c>. The token is only in the first response; replays omit it.</summary>
    [HttpPost]
    [HasPermission(Permissions.BookingCreate)]
    [ProducesResponseType<HoldDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateHoldRequest request, CancellationToken cancellationToken)
    {
        var idempotency = this.CreateIdempotencyRequest(CreateRoute, request);
        var result = await holds.CreateAsync(request, idempotency, cancellationToken);
        return this.IdempotentCreated(result, h => $"{CreateRoute}/{h.Id}");
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.BookingCreate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Release(Guid id, CancellationToken cancellationToken)
    {
        await holds.ReleaseAsync(id, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/resource-blocks")]
[Produces("application/json")]
public sealed class ResourceBlocksController(BlockService blocks) : ControllerBase
{
    /// <summary>Blocks of a venue overlapping <c>[from, to)</c> (instants). Cancelled blocks only with <c>includeCancelled=true</c>.</summary>
    [HttpGet]
    [HasPermission(Permissions.ResourceView)]
    public Task<IReadOnlyList<BlockDto>> List(
        [FromQuery] Guid venueId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] bool includeCancelled, CancellationToken cancellationToken) =>
        blocks.ListAsync(venueId, from?.ToUniversalTime(), to?.ToUniversalTime(), includeCancelled, cancellationToken);

    [HttpPost]
    [HasPermission(Permissions.ResourceUpdate)]
    [ProducesResponseType<BlockDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<BlockDto>> Create(CreateBlockRequest request, CancellationToken cancellationToken)
    {
        var block = await blocks.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = block.Id }, block);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.ResourceView)]
    public Task<BlockDto> Get(Guid id, CancellationToken cancellationToken) => blocks.GetAsync(id, cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.ResourceUpdate)]
    public Task<BlockDto> Cancel(Guid id, CancellationToken cancellationToken) => blocks.CancelAsync(id, cancellationToken);
}

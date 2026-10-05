using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Venues;

namespace BookingPlatform.Application.Bookings;

/// <summary>Everything the staff booking board shows for one venue-local business date.</summary>
public sealed record BoardDto(
    Guid VenueId,
    DateOnly Date,
    string TimeZone,
    bool IsClosed,
    IReadOnlyList<SchedulePeriodDto> Periods,
    int SlotIntervalMinutes,
    int TurnoverMinutes,
    IReadOnlyList<BookingDto> Bookings,
    IReadOnlyList<BlockDto> Blocks,
    bool Truncated);

/// <summary>
/// One round trip for the board: the day's operating periods, every booking of the business date (all statuses, so the
/// client can show or hide cancelled ones) and the active blocks overlapping the day.
/// </summary>
public sealed class BoardService(
    VenueService venueService,
    IBookingRepository bookings,
    IBlockRepository blocks,
    ITenantContext tenantContext)
{
    public const int MaxBookings = 1000;

    public async Task<BoardDto> GetAsync(Guid venueId, DateOnly date, CancellationToken cancellationToken)
    {
        if (date == default)
        {
            throw new ValidationAppException("date", "A date is required.");
        }

        var schedule = await venueService.GetScheduleAsync(venueId, date, date, cancellationToken);
        var venue = await venueService.GetAsync(venueId, cancellationToken);
        var day = schedule.Days[0];
        var tenantId = tenantContext.TenantId;

        var query = new BookingListQuery(venueId, date, null, null, null, null, null, null, null, null, new PageRequest(1, MaxBookings));
        var page = await bookings.ListAsync(tenantId, query, [venueId], cancellationToken);
        var dayBookings = page.Items.Select(BookingService.ToDto).ToList();

        var starts = day.Periods.Select(p => p.StartsAt).Concat(dayBookings.Select(b => b.StartsAt)).ToList();
        var ends = day.Periods.Select(p => p.EndsAt).Concat(dayBookings.Select(b => b.EndsAt)).ToList();
        IReadOnlyList<BlockDto> dayBlocks = starts.Count == 0
            ? []
            : await blocks.ListAsync(tenantId, venueId, starts.Min(), ends.Max(), false, cancellationToken);

        return new BoardDto(venueId, date, schedule.TimeZone, day.IsClosed, day.Periods, venue.Rules.SlotIntervalMinutes,
            venue.Rules.TurnoverBufferMinutes, dayBookings, dayBlocks, page.TotalCount > dayBookings.Count);
    }
}

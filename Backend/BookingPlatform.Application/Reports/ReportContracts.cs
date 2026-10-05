using FluentValidation;

namespace BookingPlatform.Application.Reports;

/// <summary>A venue and an inclusive range of business dates (venue-local).</summary>
public sealed record ReportQuery(Guid VenueId, DateOnly From, DateOnly To);

public sealed record CountByKeyDto(string Key, int Bookings, int Covers);

public sealed record DailyReportPointDto(DateOnly Date, int Bookings, int Covers, int Cancelled, int NoShows);

/// <param name="Hour">Venue-local hour of the booking start (0-23).</param>
public sealed record HourlyReportPointDto(int Hour, int Bookings, int Covers);

/// <summary>Deposits for bookings in the range, per currency. Amounts are minor units.</summary>
public sealed record DepositReportDto(string Currency, int PaidCount, long CollectedMinor, long RefundedMinor);

/// <summary>
/// Booking analytics for one venue. <c>Bookings</c> and <c>Covers</c> exclude cancelled bookings; rates are fractions
/// (0-1): cancellations over all bookings, no-shows over non-cancelled bookings (D-090).
/// </summary>
public sealed record BookingsSummaryDto(
    Guid VenueId,
    string VenueName,
    string TimeZone,
    DateOnly From,
    DateOnly To,
    int TotalBookings,
    int Bookings,
    int Covers,
    int Cancelled,
    int NoShows,
    decimal CancellationRate,
    decimal NoShowRate,
    decimal AveragePartySize,
    IReadOnlyList<CountByKeyDto> ByStatus,
    IReadOnlyList<CountByKeyDto> BySource,
    IReadOnlyList<DailyReportPointDto> Daily,
    IReadOnlyList<HourlyReportPointDto> ByHour,
    IReadOnlyList<DepositReportDto> Deposits);

public sealed record BookingsSummaryData(
    IReadOnlyList<CountByKeyDto> ByStatus,
    IReadOnlyList<CountByKeyDto> BySource,
    IReadOnlyList<DailyReportPointDto> Daily,
    IReadOnlyList<HourlyReportPointDto> ByHour,
    IReadOnlyList<DepositReportDto> Deposits);

public sealed class BookingExportRow
{
    public string Reference { get; init; } = string.Empty;
    public DateOnly BusinessDate { get; init; }
    public string LocalTime { get; init; } = string.Empty;
    public int DurationMinutes { get; init; }
    public int PartySize { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string? GuestName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CancelledAt { get; init; }
}

public sealed record CsvFile(string FileName, byte[] Content);

public interface IReportRepository
{
    Task<BookingsSummaryData> GetBookingsSummaryAsync(Guid tenantId, Guid venueId, DateOnly from, DateOnly to, string timeZone, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="limit"/> bookings ordered by start time.</summary>
    Task<IReadOnlyList<BookingExportRow>> ListBookingsForExportAsync(
        Guid tenantId, Guid venueId, DateOnly from, DateOnly to, string timeZone, int limit, CancellationToken cancellationToken);
}

public sealed class ReportQueryValidator : AbstractValidator<ReportQuery>
{
    public const int MaxRangeDays = 366;

    public ReportQueryValidator()
    {
        RuleFor(q => q.VenueId).NotEmpty();
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).WithMessage("'to' must be on or after 'from'.");
        RuleFor(q => q)
            .Must(q => q.To.DayNumber - q.From.DayNumber < MaxRangeDays)
            .OverridePropertyName("to")
            .WithMessage($"The report range can cover at most {MaxRangeDays} days.")
            .When(q => q.To >= q.From);
    }
}

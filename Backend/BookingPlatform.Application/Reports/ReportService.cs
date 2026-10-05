using System.Globalization;
using System.Text;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Common.Validation;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Bookings;
using FluentValidation;

namespace BookingPlatform.Application.Reports;

/// <summary>
/// Read-only booking analytics computed on demand from the booking tables (no read-model tables, D-090). Every query is
/// scoped to the tenant and to a venue the caller can access.
/// </summary>
public sealed class ReportService(
    IReportRepository reports,
    IVenueRepository venues,
    IVenueAccessGuard guard,
    ITenantContext tenantContext,
    IValidator<ReportQuery> validator)
{
    public const int MaxExportRows = 20_000;

    public async Task<BookingsSummaryDto> GetBookingsSummaryAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var venue = await ResolveAsync(query, cancellationToken);
        var data = await reports.GetBookingsSummaryAsync(tenantContext.TenantId, venue.Id, query.From, query.To, venue.TimeZone, cancellationToken);

        var total = data.ByStatus.Sum(s => s.Bookings);
        var cancelled = data.ByStatus.Where(s => s.Key == BookingStatus.Cancelled).Sum(s => s.Bookings);
        var noShows = data.ByStatus.Where(s => s.Key == BookingStatus.NoShow).Sum(s => s.Bookings);
        var active = total - cancelled;
        var covers = data.ByStatus.Where(s => s.Key != BookingStatus.Cancelled).Sum(s => s.Covers);

        return new BookingsSummaryDto(venue.Id, venue.Name, venue.TimeZone, query.From, query.To, total, active, covers, cancelled, noShows,
            Ratio(cancelled, total), Ratio(noShows, active), active == 0 ? 0 : Math.Round((decimal)covers / active, 2),
            data.ByStatus, data.BySource, data.Daily, data.ByHour, data.Deposits);
    }

    /// <summary>Bookings in the range as CSV (no contact details). Cells that a spreadsheet would evaluate are neutralised.</summary>
    public async Task<CsvFile> ExportBookingsCsvAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        var venue = await ResolveAsync(query, cancellationToken);
        var rows = await reports.ListBookingsForExportAsync(tenantContext.TenantId, venue.Id, query.From, query.To, venue.TimeZone,
            MaxExportRows + 1, cancellationToken);
        if (rows.Count > MaxExportRows)
        {
            throw new BusinessRuleAppException(ErrorCodes.BusinessRuleViolation,
                $"The export is limited to {MaxExportRows:N0} bookings. Choose a shorter date range.");
        }

        var csv = new StringBuilder();
        csv.Append("Reference,Date,Time,Duration (min),Party size,Status,Source,Guest,Created (UTC),Cancelled (UTC)\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(',',
                Cell(row.Reference),
                Cell(row.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Cell(row.LocalTime),
                row.DurationMinutes.ToString(CultureInfo.InvariantCulture),
                row.PartySize.ToString(CultureInfo.InvariantCulture),
                Cell(row.Status),
                Cell(row.Source),
                Cell(row.GuestName),
                Cell(row.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Cell(row.CancelledAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            csv.Append("\r\n");
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return new CsvFile($"bookings-{venue.Slug}-{query.From:yyyyMMdd}-{query.To:yyyyMMdd}.csv", bytes);
    }

    /// <summary>
    /// Quotes every text cell and prefixes values starting with a formula trigger (= + - @ tab CR) with an apostrophe,
    /// so guest-supplied text cannot run as a spreadsheet formula (CSV injection).
    /// </summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private async Task<VenueDto> ResolveAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await validator.EnsureValidAsync(query, cancellationToken);
        await guard.EnsureAccessAsync(query.VenueId, cancellationToken);
        return await venues.GetAsync(tenantContext.TenantId, query.VenueId, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.VenueNotFound, "The venue was not found.");
    }

    private static decimal Ratio(int part, int whole) => whole == 0 ? 0 : Math.Round((decimal)part / whole, 4);
}

using BookingPlatform.Application.Reports;
using BookingPlatform.Infrastructure.Persistence;

namespace BookingPlatform.Infrastructure.Reports;

/// <summary>Aggregates over <c>bookings</c> using the (tenant_id, venue_id, business_date) indexes.</summary>
public sealed class ReportRepository(DbSession db) : IReportRepository
{
    private const string InRange = "b.tenant_id = @tenantId AND b.venue_id = @venueId AND b.business_date BETWEEN @from AND @to";

    public async Task<BookingsSummaryData> GetBookingsSummaryAsync(
        Guid tenantId, Guid venueId, DateOnly from, DateOnly to, string timeZone, CancellationToken cancellationToken)
    {
        await using var grid = await db.QueryMultipleAsync(
            $"""
            SELECT b.status AS key, count(*)::int AS bookings, coalesce(sum(b.party_size), 0)::int AS covers
            FROM bookings b WHERE {InRange}
            GROUP BY b.status ORDER BY b.status;

            SELECT b.source AS key, count(*)::int AS bookings, coalesce(sum(b.party_size), 0)::int AS covers
            FROM bookings b WHERE {InRange} AND b.status <> 'CANCELLED'
            GROUP BY b.source ORDER BY b.source;

            SELECT d::date AS date,
                   count(b.id) FILTER (WHERE b.status <> 'CANCELLED')::int AS bookings,
                   coalesce(sum(b.party_size) FILTER (WHERE b.status <> 'CANCELLED'), 0)::int AS covers,
                   count(b.id) FILTER (WHERE b.status = 'CANCELLED')::int AS cancelled,
                   count(b.id) FILTER (WHERE b.status = 'NO_SHOW')::int AS no_shows
            FROM generate_series(@from::date, @to::date, interval '1 day') AS d
            LEFT JOIN bookings b ON b.tenant_id = @tenantId AND b.venue_id = @venueId AND b.business_date = d::date
            GROUP BY d ORDER BY d;

            SELECT extract(hour FROM b.starts_at AT TIME ZONE @timeZone)::int AS hour,
                   count(*)::int AS bookings, coalesce(sum(b.party_size), 0)::int AS covers
            FROM bookings b WHERE {InRange} AND b.status <> 'CANCELLED'
            GROUP BY 1 ORDER BY 1;

            SELECT p.currency::text AS currency,
                   count(*) FILTER (WHERE p.status = 'SUCCEEDED')::int AS paid_count,
                   coalesce(sum(p.amount_minor) FILTER (WHERE p.status = 'SUCCEEDED'), 0)::bigint AS collected_minor,
                   coalesce(sum(p.refunded_amount_minor), 0)::bigint AS refunded_minor
            FROM bookings b
            JOIN payments p ON p.tenant_id = b.tenant_id AND p.booking_id = b.id
            WHERE {InRange}
            GROUP BY p.currency ORDER BY p.currency;
            """,
            new { tenantId, venueId, from, to, timeZone },
            cancellationToken);

        var byStatus = (await grid.ReadAsync<CountByKeyDto>()).ToList();
        var bySource = (await grid.ReadAsync<CountByKeyDto>()).ToList();
        var daily = (await grid.ReadAsync<DailyRow>()).Select(r => new DailyReportPointDto(r.Date, r.Bookings, r.Covers, r.Cancelled, r.NoShows)).ToList();
        var byHour = (await grid.ReadAsync<HourlyReportPointDto>()).ToList();
        var deposits = (await grid.ReadAsync<DepositReportDto>()).ToList();
        return new BookingsSummaryData(byStatus, bySource, daily, byHour, deposits);
    }

    public Task<IReadOnlyList<BookingExportRow>> ListBookingsForExportAsync(
        Guid tenantId, Guid venueId, DateOnly from, DateOnly to, string timeZone, int limit, CancellationToken cancellationToken) =>
        db.QueryAsync<BookingExportRow>(
            $"""
            SELECT b.reference, b.business_date,
                   to_char(b.starts_at AT TIME ZONE @timeZone, 'HH24:MI') AS local_time,
                   (extract(epoch FROM b.ends_at - b.starts_at) / 60)::int AS duration_minutes,
                   b.party_size, b.status, b.source,
                   nullif(btrim(c.first_name || ' ' || coalesce(c.last_name, '')), '') AS guest_name,
                   b.created_at, b.cancelled_at
            FROM bookings b
            LEFT JOIN customers c ON c.tenant_id = b.tenant_id AND c.id = b.customer_id
            WHERE {InRange}
            ORDER BY b.starts_at, b.reference
            LIMIT @limit
            """,
            new { tenantId, venueId, from, to, timeZone, limit },
            cancellationToken);

    private sealed class DailyRow
    {
        public DateOnly Date { get; init; }
        public int Bookings { get; init; }
        public int Covers { get; init; }
        public int Cancelled { get; init; }
        public int NoShows { get; init; }
    }
}

using NodaTime;
using NodaTime.TimeZones;

namespace BookingPlatform.Domain.Venues;

/// <summary>A weekly opening period. <see cref="Close"/> at or before <see cref="Open"/> means it ends the next day.</summary>
public sealed record WeeklyPeriod(IsoDayOfWeek Day, LocalTime Open, LocalTime Close);

/// <summary>A date override: either closed all day or one custom period. Several rows may exist for one date.</summary>
public sealed record SpecialPeriod(LocalDate Date, bool IsClosed, LocalTime? Open, LocalTime? Close);

/// <summary>An effective opening period for one business date, resolved to UTC instants.</summary>
public sealed record OperatingPeriod(LocalDate BusinessDate, LocalTime Open, LocalTime Close, Instant Start, Instant End, bool IsSpecial)
{
    public bool IsOvernight => Close <= Open;

    public bool Contains(Instant start, Instant end) => start >= Start && end <= End && end > start;
}

/// <summary>
/// Venue opening hours in the venue's time zone. A business date is the local date on which a period opens, so
/// Friday 18:00 → 02:00 belongs to Friday. Special rows for a date replace the weekly schedule for that date.
/// Local times that do not exist (DST gap) shift forward to the first valid instant; ambiguous times (DST overlap)
/// resolve to the earlier offset.
/// </summary>
public sealed class OperatingSchedule
{
    private readonly DateTimeZone _zone;
    private readonly ILookup<IsoDayOfWeek, WeeklyPeriod> _weekly;
    private readonly ILookup<LocalDate, SpecialPeriod> _special;

    public OperatingSchedule(DateTimeZone zone, IEnumerable<WeeklyPeriod> weekly, IEnumerable<SpecialPeriod> special)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _zone = zone;
        _weekly = weekly.ToLookup(p => p.Day);
        _special = special.ToLookup(p => p.Date);
    }

    public DateTimeZone Zone => _zone;

    /// <summary>Effective periods that open on <paramref name="businessDate"/>, ordered by start. Empty when closed.</summary>
    public IReadOnlyList<OperatingPeriod> GetPeriods(LocalDate businessDate)
    {
        var special = _special[businessDate].ToList();
        if (special.Count > 0)
        {
            if (special.Any(s => s.IsClosed))
            {
                return [];
            }

            return special
                .Where(s => s is { Open: not null, Close: not null })
                .Select(s => Resolve(businessDate, s.Open!.Value, s.Close!.Value, isSpecial: true))
                .OrderBy(p => p.Start)
                .ToList();
        }

        return _weekly[businessDate.DayOfWeek]
            .Select(p => Resolve(businessDate, p.Open, p.Close, isSpecial: false))
            .OrderBy(p => p.Start)
            .ToList();
    }

    public bool IsSpecialDate(LocalDate date) => _special[date].Any();

    public bool IsClosedAllDay(LocalDate date) => GetPeriods(date).Count == 0;

    /// <summary>The period that fully contains <c>[start, end)</c>, checking the local date of start and the day before (overnight).</summary>
    public OperatingPeriod? FindContaining(Instant start, Instant end)
    {
        if (end <= start)
        {
            return null;
        }

        var localDate = start.InZone(_zone).Date;
        foreach (var date in new[] { localDate, localDate.PlusDays(-1) })
        {
            var match = GetPeriods(date).FirstOrDefault(p => p.Contains(start, end));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    public Instant ToInstant(LocalDate date, LocalTime time) => _zone.ResolveLocal(date.At(time), Resolvers.LenientResolver).ToInstant();

    private OperatingPeriod Resolve(LocalDate businessDate, LocalTime open, LocalTime close, bool isSpecial)
    {
        var start = ToInstant(businessDate, open);
        var endDate = close <= open ? businessDate.PlusDays(1) : businessDate;
        var end = ToInstant(endDate, close);
        return new OperatingPeriod(businessDate, open, close, start, end, isSpecial);
    }

    /// <summary>
    /// Validates a weekly schedule: no period may overlap another, including overnight periods that run into the
    /// next day (and Sunday into Monday). Returns an error message or null.
    /// </summary>
    public static string? ValidateWeekly(IReadOnlyCollection<WeeklyPeriod> periods)
    {
        const int minutesPerDay = 24 * 60;
        const int minutesPerWeek = 7 * minutesPerDay;

        var intervals = new List<(int Start, int End, WeeklyPeriod Period)>();
        foreach (var period in periods)
        {
            if (period.Open == period.Close)
            {
                return $"{period.Day}: opening and closing times must differ.";
            }

            var start = ((int)period.Day - 1) * minutesPerDay + period.Open.Hour * 60 + period.Open.Minute;
            var closeMinutes = period.Close.Hour * 60 + period.Close.Minute;
            var openMinutes = period.Open.Hour * 60 + period.Open.Minute;
            var length = closeMinutes > openMinutes ? closeMinutes - openMinutes : minutesPerDay - openMinutes + closeMinutes;
            var end = start + length;

            if (end <= minutesPerWeek)
            {
                intervals.Add((start, end, period));
            }
            else
            {
                intervals.Add((start, minutesPerWeek, period));
                intervals.Add((0, end - minutesPerWeek, period));
            }
        }

        var ordered = intervals.OrderBy(i => i.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start < ordered[i - 1].End)
            {
                var a = ordered[i - 1].Period;
                var b = ordered[i].Period;
                return $"Opening periods overlap: {a.Day} {a.Open:HH:mm}-{a.Close:HH:mm} and {b.Day} {b.Open:HH:mm}-{b.Close:HH:mm}.";
            }
        }

        return null;
    }

    /// <summary>Validates custom periods for one date: they must not overlap each other. Returns an error message or null.</summary>
    public static string? ValidateSameDay(IReadOnlyCollection<(LocalTime Open, LocalTime Close)> periods)
    {
        const int minutesPerDay = 24 * 60;
        var intervals = periods
            .Select(p =>
            {
                var open = p.Open.Hour * 60 + p.Open.Minute;
                var close = p.Close.Hour * 60 + p.Close.Minute;
                return (Start: open, End: close > open ? close : minutesPerDay + close, p.Open, p.Close);
            })
            .OrderBy(p => p.Start)
            .ToList();

        for (var i = 1; i < intervals.Count; i++)
        {
            if (intervals[i].Start < intervals[i - 1].End)
            {
                return $"Opening periods overlap: {intervals[i - 1].Open:HH:mm}-{intervals[i - 1].Close:HH:mm} and {intervals[i].Open:HH:mm}-{intervals[i].Close:HH:mm}.";
            }
        }

        return null;
    }
}

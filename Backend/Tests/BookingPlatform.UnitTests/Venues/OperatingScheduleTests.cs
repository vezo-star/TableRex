using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Time;
using BookingPlatform.Domain.Venues;
using NodaTime;
using NodaTime.Text;

namespace BookingPlatform.UnitTests.Venues;

public sealed class OperatingScheduleTests
{
    private static readonly DateTimeZone Johannesburg = TimeZones.Get("Africa/Johannesburg");
    private static readonly DateTimeZone London = TimeZones.Get("Europe/London");

    private static LocalTime T(string value) => LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(value).Value;

    private static LocalDate D(string value) => LocalDatePattern.Iso.Parse(value).Value;

    private static Instant I(string value) => InstantPattern.General.Parse(value).Value;

    private static OperatingSchedule Schedule(DateTimeZone zone, IEnumerable<WeeklyPeriod> weekly, params SpecialPeriod[] special) =>
        new(zone, weekly, special);

    [Fact]
    public void Converts_a_same_day_period_to_utc_in_the_venue_time_zone()
    {
        var schedule = Schedule(Johannesburg, [new WeeklyPeriod(IsoDayOfWeek.Monday, T("18:00"), T("22:00"))]);

        var periods = schedule.GetPeriods(D("2026-10-05"));

        periods.Should().ContainSingle();
        periods[0].Start.Should().Be(I("2026-10-05T16:00:00Z"));
        periods[0].End.Should().Be(I("2026-10-05T20:00:00Z"));
        periods[0].IsOvernight.Should().BeFalse();
    }

    [Fact]
    public void Overnight_period_belongs_to_the_business_date_it_opens_on()
    {
        var schedule = Schedule(Johannesburg, [new WeeklyPeriod(IsoDayOfWeek.Friday, T("18:00"), T("02:00"))]);

        var friday = schedule.GetPeriods(D("2026-10-09"));
        var saturday = schedule.GetPeriods(D("2026-10-10"));

        friday.Should().ContainSingle();
        friday[0].End.Should().Be(I("2026-10-10T00:00:00Z"));
        friday[0].IsOvernight.Should().BeTrue();
        saturday.Should().BeEmpty();
    }

    [Fact]
    public void Finds_the_overnight_period_for_a_booking_after_midnight()
    {
        var schedule = Schedule(Johannesburg, [new WeeklyPeriod(IsoDayOfWeek.Friday, T("18:00"), T("02:00"))]);

        // Saturday 00:30-01:30 local = Friday 22:30-23:30 UTC.
        var match = schedule.FindContaining(I("2026-10-09T22:30:00Z"), I("2026-10-09T23:30:00Z"));

        match.Should().NotBeNull();
        match!.BusinessDate.Should().Be(D("2026-10-09"));
    }

    [Fact]
    public void Booking_that_runs_past_closing_is_not_contained()
    {
        var schedule = Schedule(Johannesburg, [new WeeklyPeriod(IsoDayOfWeek.Monday, T("18:00"), T("22:00"))]);

        schedule.FindContaining(I("2026-10-05T19:00:00Z"), I("2026-10-05T21:00:00Z")).Should().BeNull();
        schedule.FindContaining(I("2026-10-05T18:00:00Z"), I("2026-10-05T20:00:00Z")).Should().NotBeNull();
    }

    [Fact]
    public void Supports_several_periods_per_day()
    {
        var schedule = Schedule(Johannesburg,
        [
            new WeeklyPeriod(IsoDayOfWeek.Monday, T("18:00"), T("22:00")),
            new WeeklyPeriod(IsoDayOfWeek.Monday, T("11:00"), T("15:00")),
        ]);

        var periods = schedule.GetPeriods(D("2026-10-05"));

        periods.Select(p => p.Open).Should().Equal(T("11:00"), T("18:00"));
    }

    [Fact]
    public void Special_closed_day_replaces_the_weekly_schedule()
    {
        var schedule = Schedule(Johannesburg,
            [new WeeklyPeriod(IsoDayOfWeek.Monday, T("18:00"), T("22:00"))],
            new SpecialPeriod(D("2026-10-05"), IsClosed: true, null, null));

        schedule.GetPeriods(D("2026-10-05")).Should().BeEmpty();
        schedule.IsSpecialDate(D("2026-10-05")).Should().BeTrue();
        schedule.GetPeriods(D("2026-10-12")).Should().ContainSingle();
    }

    [Fact]
    public void Special_periods_replace_the_weekly_schedule_for_that_date()
    {
        var schedule = Schedule(Johannesburg,
            [new WeeklyPeriod(IsoDayOfWeek.Monday, T("18:00"), T("22:00"))],
            new SpecialPeriod(D("2026-10-05"), false, T("10:00"), T("14:00")));

        var periods = schedule.GetPeriods(D("2026-10-05"));

        periods.Should().ContainSingle();
        periods[0].Open.Should().Be(T("10:00"));
        periods[0].IsSpecial.Should().BeTrue();
    }

    [Fact]
    public void Special_hours_can_open_a_normally_closed_day()
    {
        var schedule = Schedule(Johannesburg, [], new SpecialPeriod(D("2026-12-31"), false, T("20:00"), T("03:00")));

        var periods = schedule.GetPeriods(D("2026-12-31"));

        periods.Should().ContainSingle();
        periods[0].End.Should().Be(I("2027-01-01T01:00:00Z"));
    }

    [Fact]
    public void Spring_forward_gap_shortens_an_overnight_period()
    {
        // Europe/London: 2026-03-29 01:00 GMT jumps to 02:00 BST.
        var schedule = Schedule(London, [new WeeklyPeriod(IsoDayOfWeek.Saturday, T("22:00"), T("03:00"))]);

        var period = schedule.GetPeriods(D("2026-03-28")).Single();

        period.Start.Should().Be(I("2026-03-28T22:00:00Z"));
        period.End.Should().Be(I("2026-03-29T02:00:00Z"));
        (period.End - period.Start).Should().Be(Duration.FromHours(4));
    }

    [Fact]
    public void Nonexistent_local_opening_time_shifts_forward()
    {
        var schedule = Schedule(London, [new WeeklyPeriod(IsoDayOfWeek.Sunday, T("01:30"), T("05:00"))]);

        var period = schedule.GetPeriods(D("2026-03-29")).Single();

        // 01:30 does not exist; shifted by the one-hour gap to 02:30 BST = 01:30 UTC.
        period.Start.Should().Be(I("2026-03-29T01:30:00Z"));
        period.End.Should().Be(I("2026-03-29T04:00:00Z"));
    }

    [Fact]
    public void Ambiguous_local_time_resolves_to_the_earlier_offset()
    {
        // Europe/London: 2026-10-25 02:00 BST falls back to 01:00 GMT, so 01:30 happens twice.
        var schedule = Schedule(London, [new WeeklyPeriod(IsoDayOfWeek.Sunday, T("01:30"), T("04:00"))]);

        var period = schedule.GetPeriods(D("2026-10-25")).Single();

        period.Start.Should().Be(I("2026-10-25T00:30:00Z"));
        period.End.Should().Be(I("2026-10-25T04:00:00Z"));
    }

    [Fact]
    public void Fall_back_lengthens_an_overnight_period()
    {
        var schedule = Schedule(London, [new WeeklyPeriod(IsoDayOfWeek.Saturday, T("22:00"), T("03:00"))]);

        var period = schedule.GetPeriods(D("2026-10-24")).Single();

        (period.End - period.Start).Should().Be(Duration.FromHours(6));
    }

    [Fact]
    public void Weekly_validation_accepts_back_to_back_and_separate_periods() =>
        OperatingSchedule.ValidateWeekly(
        [
            new WeeklyPeriod(IsoDayOfWeek.Monday, T("10:00"), T("14:00")),
            new WeeklyPeriod(IsoDayOfWeek.Monday, T("14:00"), T("18:00")),
            new WeeklyPeriod(IsoDayOfWeek.Monday, T("19:00"), T("02:00")),
            new WeeklyPeriod(IsoDayOfWeek.Tuesday, T("02:00"), T("06:00")),
        ]).Should().BeNull();

    [Fact]
    public void Weekly_validation_rejects_overlap_on_the_same_day() =>
        OperatingSchedule.ValidateWeekly(
        [
            new WeeklyPeriod(IsoDayOfWeek.Monday, T("10:00"), T("15:00")),
            new WeeklyPeriod(IsoDayOfWeek.Monday, T("14:00"), T("18:00")),
        ]).Should().NotBeNull();

    [Fact]
    public void Weekly_validation_rejects_overnight_overlap_into_the_next_day() =>
        OperatingSchedule.ValidateWeekly(
        [
            new WeeklyPeriod(IsoDayOfWeek.Monday, T("18:00"), T("02:00")),
            new WeeklyPeriod(IsoDayOfWeek.Tuesday, T("01:00"), T("10:00")),
        ]).Should().NotBeNull();

    [Fact]
    public void Weekly_validation_rejects_sunday_overnight_overlapping_monday() =>
        OperatingSchedule.ValidateWeekly(
        [
            new WeeklyPeriod(IsoDayOfWeek.Sunday, T("20:00"), T("04:00")),
            new WeeklyPeriod(IsoDayOfWeek.Monday, T("03:00"), T("10:00")),
        ]).Should().NotBeNull();

    [Fact]
    public void Same_day_validation_rejects_overlapping_custom_periods()
    {
        OperatingSchedule.ValidateSameDay([(T("10:00"), T("14:00")), (T("13:00"), T("16:00"))]).Should().NotBeNull();
        OperatingSchedule.ValidateSameDay([(T("10:00"), T("14:00")), (T("14:00"), T("16:00"))]).Should().BeNull();
    }
}

public sealed class VenueValidatorTests
{
    private static CreateVenueRequest Venue(string slug = "main-venue", string timeZone = "Africa/Johannesburg", VenueBookingRules? rules = null) =>
        new("Main venue", slug, timeZone, null, null, null, null, null, null, "ZA", null, null, rules);

    [Fact]
    public void Accepts_a_valid_venue() =>
        new CreateVenueRequestValidator().Validate(Venue()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("Main Venue")]
    [InlineData("-main")]
    [InlineData("main--venue")]
    public void Rejects_invalid_slugs(string slug) =>
        new CreateVenueRequestValidator().Validate(Venue(slug)).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_unknown_time_zones() =>
        new CreateVenueRequestValidator().Validate(Venue(timeZone: "Mars/Olympus")).IsValid.Should().BeFalse();

    [Fact]
    public void Rejects_inconsistent_booking_rules()
    {
        var rules = VenueBookingRules.Default with { MinPartySize = 10, MaxPartySize = 4 };

        new CreateVenueRequestValidator().Validate(Venue(rules: rules)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_default_duration_outside_min_and_max()
    {
        var rules = VenueBookingRules.Default with { DefaultDurationMinutes = 600 };

        new VenueBookingRulesValidator().Validate(rules).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Resource_capacity_must_cover_the_minimum()
    {
        var request = new CreateResourceRequest(Guid.NewGuid(), null, Guid.NewGuid(), "Table 1", "T1", MinCapacity: 6, Capacity: 4);

        new CreateResourceRequestValidator().Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Floor_plan_positions_reject_duplicates_and_bad_shapes()
    {
        var id = Guid.NewGuid();
        var request = new ReplaceFloorPlanPositionsRequest(
        [
            new FloorPlanPositionDto(id, 10, 10, 50, 50, 0, "RECT"),
            new FloorPlanPositionDto(id, 100, 10, 50, 50, 0, "STAR"),
        ]);

        var result = new ReplaceFloorPlanPositionsRequestValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(2);
    }
}

using BookingPlatform.Domain.Availability;
using BookingPlatform.Domain.Time;
using BookingPlatform.Domain.Venues;
using NodaTime;
using NodaTime.Text;

namespace BookingPlatform.UnitTests.Availability;

public sealed class AvailabilityCalculatorTests
{
    private static readonly DateTimeZone Johannesburg = TimeZones.Get("Africa/Johannesburg");
    private static readonly DateTimeZone London = TimeZones.Get("Europe/London");

    private static readonly LocalDate Monday = new(2026, 10, 5);
    private static readonly LocalDate Friday = new(2026, 10, 9);

    private static readonly AvailabilityResource T1 = new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "T1", 1, 4, 1, null);
    private static readonly AvailabilityResource T2 = new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "T2", 1, 4, 2, null);
    private static readonly AvailabilityResource T3 = new(Guid.Parse("00000000-0000-0000-0000-000000000003"), "T3", 1, 2, 3, null);
    private static readonly AvailabilityResource Booth = new(Guid.Parse("00000000-0000-0000-0000-000000000004"), "Booth", 5, 8, 4, null);

    private static LocalTime T(string value) => LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(value).Value;

    private static Instant I(string value) => InstantPattern.General.Parse(value).Value;

    private static OperatingSchedule Schedule(DateTimeZone zone, IEnumerable<WeeklyPeriod> weekly, params SpecialPeriod[] special) =>
        new(zone, weekly, special);

    private static OperatingSchedule MondayEvenings(string open = "17:00", string close = "23:00") =>
        Schedule(Johannesburg, [new WeeklyPeriod(IsoDayOfWeek.Monday, T(open), T(close))]);

    private static AvailabilityQuery Query(LocalDate date, int party = 2, int duration = 120, int interval = 30, int turnover = 0, Instant? earliest = null) =>
        new(date, party, Duration.FromMinutes(duration), Duration.FromMinutes(interval), Duration.FromMinutes(turnover), earliest);

    private static ResourceClaim Claim(AvailabilityResource resource, string start, string end) => new(resource.Id, I(start), I(end));

    private static IReadOnlyList<AvailabilitySlot> Calculate(
        OperatingSchedule schedule,
        AvailabilityQuery query,
        IReadOnlyCollection<AvailabilityResource> resources,
        IReadOnlyCollection<ResourceClaim>? claims = null,
        IReadOnlyCollection<AvailabilityCombination>? combinations = null) =>
        AvailabilityCalculator.Calculate(schedule, query, resources, combinations ?? [], claims ?? []);

    private static AvailabilitySlot At(IReadOnlyList<AvailabilitySlot> slots, string localTime) => slots.Single(s => s.LocalTime == T(localTime));

    private static IEnumerable<Guid> OfferedIds(AvailabilitySlot slot) => slot.Options.Select(o => o.Id);

    [Fact]
    public void Generates_slots_every_interval_that_fit_inside_the_period()
    {
        var slots = Calculate(MondayEvenings("18:00", "22:00"), Query(Monday), [T1]);

        slots.Select(s => s.LocalTime).Should().Equal(T("18:00"), T("18:30"), T("19:00"), T("19:30"), T("20:00"));
        slots[0].Start.Should().Be(I("2026-10-05T16:00:00Z"));
        slots[0].End.Should().Be(I("2026-10-05T18:00:00Z"));
        slots.Should().OnlyContain(s => s.IsAvailable);
    }

    [Fact]
    public void Closed_day_has_no_slots()
    {
        Calculate(MondayEvenings(), Query(Monday.PlusDays(1)), [T1]).Should().BeEmpty();
    }

    [Fact]
    public void Claims_block_overlapping_slots_and_adjacent_slots_stay_free()
    {
        var claims = new[] { Claim(T1, "2026-10-05T17:00:00Z", "2026-10-05T19:00:00Z") }; // 19:00-21:00 local

        var slots = Calculate(MondayEvenings(), Query(Monday), [T1, T2], claims);

        OfferedIds(At(slots, "17:00")).Should().Contain(T1.Id, "[17:00, 19:00) only touches the claim");
        OfferedIds(At(slots, "17:30")).Should().NotContain(T1.Id);
        OfferedIds(At(slots, "20:30")).Should().NotContain(T1.Id);
        OfferedIds(At(slots, "21:00")).Should().Contain(T1.Id, "[21:00, 23:00) starts when the claim ends");
        OfferedIds(At(slots, "19:00")).Should().Equal(T2.Id);
    }

    [Fact]
    public void Turnover_extends_the_conflict_window_but_not_the_opening_hours_check()
    {
        var claims = new[] { Claim(T1, "2026-10-05T17:00:00Z", "2026-10-05T19:00:00Z") };

        var slots = Calculate(MondayEvenings(), Query(Monday, turnover: 15), [T1], claims);

        At(slots, "17:00").IsAvailable.Should().BeFalse("17:00 + 2h + 15m overlaps the 19:00 claim");
        At(slots, "21:00").IsAvailable.Should().BeTrue("the last slot may end at closing; turnover runs past it");
    }

    [Fact]
    public void Only_resources_whose_capacity_range_fits_the_party_are_offered()
    {
        var slots = Calculate(MondayEvenings(), Query(Monday, party: 6), [T1, T2, T3, Booth]);
        OfferedIds(slots[0]).Should().Equal(Booth.Id);

        var small = Calculate(MondayEvenings(), Query(Monday, party: 2), [T1, T3, Booth]);
        OfferedIds(small[0]).Should().Equal([T3.Id, T1.Id], "the booth's minimum capacity is 5; the smallest table ranks first");
    }

    [Fact]
    public void Combination_is_offered_only_when_all_required_members_are_free()
    {
        var combination = new AvailabilityCombination(Guid.NewGuid(), "T1+T2", 5, 8, 0,
            [new CombinationMember(T1.Id, true), new CombinationMember(T2.Id, true)]);
        var claims = new[] { Claim(T2, "2026-10-05T17:00:00Z", "2026-10-05T19:00:00Z") };

        var slots = Calculate(MondayEvenings(), Query(Monday, party: 7), [T1, T2], claims, [combination]);

        var free = At(slots, "17:00").Options.Should().ContainSingle().Subject;
        free.Kind.Should().Be(AvailabilityOptionKind.Combination);
        free.ResourceIds.Should().BeEquivalentTo([T1.Id, T2.Id]);
        At(slots, "19:00").IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Optional_members_are_allocated_only_when_free()
    {
        var combination = new AvailabilityCombination(Guid.NewGuid(), "T1+T2(+T3)", 5, 10, 0,
            [new CombinationMember(T1.Id, true), new CombinationMember(T2.Id, true), new CombinationMember(T3.Id, false)]);
        var claims = new[] { Claim(T3, "2026-10-05T17:00:00Z", "2026-10-05T19:00:00Z") };

        var slots = Calculate(MondayEvenings(), Query(Monday, party: 8), [T1, T2, T3], claims, [combination]);

        At(slots, "17:00").Options.Single().ResourceIds.Should().BeEquivalentTo([T1.Id, T2.Id, T3.Id]);
        At(slots, "19:00").Options.Single().ResourceIds.Should().BeEquivalentTo([T1.Id, T2.Id]);
    }

    [Fact]
    public void Combination_with_a_required_member_outside_the_eligible_set_is_not_offered()
    {
        var combination = new AvailabilityCombination(Guid.NewGuid(), "T1+T2", 5, 8, 0,
            [new CombinationMember(T1.Id, true), new CombinationMember(T2.Id, true)]);

        var slots = Calculate(MondayEvenings(), Query(Monday, party: 6), [T1], combinations: [combination]);

        slots.Should().OnlyContain(s => !s.IsAvailable);
    }

    [Fact]
    public void Ranks_single_resources_before_combinations_then_by_capacity_then_sort_order()
    {
        var bigTable = new AvailabilityResource(Guid.NewGuid(), "Big", 1, 10, 9, null);
        var sameCapacityLowerSort = new AvailabilityResource(Guid.NewGuid(), "Z-table", 1, 10, 0, null);
        var combination = new AvailabilityCombination(Guid.NewGuid(), "T1+T2", 1, 8, 0,
            [new CombinationMember(T1.Id, true), new CombinationMember(T2.Id, true)]);

        var slot = Calculate(MondayEvenings(), Query(Monday, party: 4), [bigTable, sameCapacityLowerSort, T1, T2], combinations: [combination])[0];

        slot.Options.Select(o => o.Name).Should().Equal("T1", "T2", "Z-table", "Big", "T1+T2");
    }

    [Fact]
    public void Overnight_slots_belong_to_the_business_date_the_period_opens_on()
    {
        var schedule = Schedule(Johannesburg, [new WeeklyPeriod(IsoDayOfWeek.Friday, T("20:00"), T("02:00"))]);

        var friday = Calculate(schedule, Query(Friday, duration: 120, interval: 60), [T1]);
        var saturday = Calculate(schedule, Query(Friday.PlusDays(1), duration: 120, interval: 60), [T1]);

        friday.Select(s => s.LocalTime).Should().Equal(T("20:00"), T("21:00"), T("22:00"), T("23:00"), T("00:00"));
        friday[^1].Start.Should().Be(I("2026-10-09T22:00:00Z"));
        friday[^1].End.Should().Be(I("2026-10-10T00:00:00Z"));
        saturday.Should().BeEmpty();
    }

    [Fact]
    public void Overnight_claim_from_the_previous_period_blocks_early_slots()
    {
        var schedule = Schedule(Johannesburg, [
            new WeeklyPeriod(IsoDayOfWeek.Friday, T("20:00"), T("02:00")),
            new WeeklyPeriod(IsoDayOfWeek.Saturday, T("02:00"), T("06:00")),
        ]);
        // Friday booking 00:00-02:00 local plus 30 minutes turnover.
        var claims = new[] { Claim(T1, "2026-10-09T22:00:00Z", "2026-10-10T00:30:00Z") };

        var saturday = Calculate(schedule, Query(Friday.PlusDays(1), duration: 60, interval: 30), [T1], claims);

        At(saturday, "02:00").IsAvailable.Should().BeFalse();
        At(saturday, "02:30").IsAvailable.Should().BeTrue();
    }

    [Fact]
    public void Special_hours_replace_or_close_the_weekly_schedule()
    {
        var closed = Schedule(Johannesburg, [new WeeklyPeriod(IsoDayOfWeek.Monday, T("17:00"), T("23:00"))],
            new SpecialPeriod(Monday, true, null, null));
        var custom = Schedule(Johannesburg, [new WeeklyPeriod(IsoDayOfWeek.Monday, T("17:00"), T("23:00"))],
            new SpecialPeriod(Monday, false, T("12:00"), T("15:00")));

        Calculate(closed, Query(Monday), [T1]).Should().BeEmpty();
        Calculate(custom, Query(Monday, interval: 60), [T1]).Select(s => s.LocalTime).Should().Equal(T("12:00"), T("13:00"));
    }

    [Fact]
    public void Slots_before_the_earliest_start_are_omitted()
    {
        var slots = Calculate(MondayEvenings("18:00", "22:00"), Query(Monday, earliest: I("2026-10-05T16:45:00Z")), [T1]);

        slots.Select(s => s.LocalTime).Should().Equal(T("19:00"), T("19:30"), T("20:00"));
    }

    [Fact]
    public void Slots_step_in_real_time_across_a_dst_gap()
    {
        var sunday = new LocalDate(2026, 3, 29); // Europe/London clocks go forward 01:00 -> 02:00
        var schedule = Schedule(London, [new WeeklyPeriod(IsoDayOfWeek.Sunday, T("00:00"), T("04:00"))]);

        var slots = Calculate(schedule, Query(sunday, duration: 60, interval: 60), [T1]);

        slots.Select(s => s.Start).Should().Equal(I("2026-03-29T00:00:00Z"), I("2026-03-29T01:00:00Z"), I("2026-03-29T02:00:00Z"));
        slots.Select(s => s.LocalTime).Should().Equal(T("00:00"), T("02:00"), T("03:00"));
    }

    [Fact]
    public void Rejects_invalid_queries()
    {
        var act = () => Calculate(MondayEvenings(), Query(Monday, party: 0), [T1]);
        act.Should().Throw<ArgumentOutOfRangeException>();

        var zeroDuration = () => Calculate(MondayEvenings(), Query(Monday, duration: 0), [T1]);
        zeroDuration.Should().Throw<ArgumentOutOfRangeException>();
    }
}

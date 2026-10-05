using BookingPlatform.Application.Bookings;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Idempotency;
using BookingPlatform.Application.Venues;
using BookingPlatform.Domain.Availability;
using BookingPlatform.Domain.Bookings;
using BookingPlatform.Domain.Time;
using BookingPlatform.Domain.Venues;
using NodaTime;

namespace BookingPlatform.UnitTests.Bookings;

public sealed class BookingStatusMachineTests
{
    public static TheoryData<string, BookingCommand, string?> Transitions => new()
    {
        { BookingStatus.Pending, BookingCommand.Confirm, BookingStatus.Confirmed },
        { BookingStatus.Held, BookingCommand.Confirm, BookingStatus.Confirmed },
        { BookingStatus.Confirmed, BookingCommand.Confirm, null },
        { BookingStatus.Confirmed, BookingCommand.CheckIn, BookingStatus.CheckedIn },
        { BookingStatus.Pending, BookingCommand.CheckIn, null },
        { BookingStatus.Confirmed, BookingCommand.Seat, BookingStatus.Seated },
        { BookingStatus.CheckedIn, BookingCommand.Seat, BookingStatus.Seated },
        { BookingStatus.Seated, BookingCommand.Complete, BookingStatus.Completed },
        { BookingStatus.Confirmed, BookingCommand.Complete, null },
        { BookingStatus.Confirmed, BookingCommand.NoShow, BookingStatus.NoShow },
        { BookingStatus.CheckedIn, BookingCommand.NoShow, null },
        { BookingStatus.Pending, BookingCommand.Cancel, BookingStatus.Cancelled },
        { BookingStatus.Held, BookingCommand.Cancel, BookingStatus.Cancelled },
        { BookingStatus.Confirmed, BookingCommand.Cancel, BookingStatus.Cancelled },
        { BookingStatus.CheckedIn, BookingCommand.Cancel, BookingStatus.Cancelled },
        { BookingStatus.Seated, BookingCommand.Cancel, null },
        { BookingStatus.Held, BookingCommand.Expire, BookingStatus.Cancelled },
        { BookingStatus.Confirmed, BookingCommand.Expire, null },
        { BookingStatus.Completed, BookingCommand.Cancel, null },
        { BookingStatus.Cancelled, BookingCommand.Cancel, null },
        { BookingStatus.NoShow, BookingCommand.Seat, null },
    };

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Applies_only_the_documented_transitions(string from, BookingCommand command, string? expected)
    {
        BookingStatusMachine.TryApply(from, command).Should().Be(expected);
        BookingStatusMachine.CanApply(from, command).Should().Be(expected is not null);
    }

    [Fact]
    public void Terminal_statuses_have_no_outgoing_transitions_and_do_not_occupy_resources()
    {
        foreach (var status in new[] { BookingStatus.Completed, BookingStatus.Cancelled, BookingStatus.NoShow })
        {
            BookingStatus.IsTerminal(status).Should().BeTrue();
            BookingStatus.IsActive(status).Should().BeFalse();
            BookingStatusMachine.IsMutable(status).Should().BeFalse();
            Enum.GetValues<BookingCommand>().Should().OnlyContain(c => !BookingStatusMachine.CanApply(status, c));
        }

        BookingStatus.Active.Should().BeEquivalentTo([BookingStatus.Pending, BookingStatus.Held, BookingStatus.Confirmed, BookingStatus.CheckedIn, BookingStatus.Seated]);
    }

    [Fact]
    public void New_bookings_start_pending_held_confirmed_or_seated()
    {
        BookingStatusMachine.InitialStatuses.Should().BeEquivalentTo([BookingStatus.Pending, BookingStatus.Held, BookingStatus.Confirmed, BookingStatus.Seated]);
        BookingStatusMachine.CanStartAs(BookingStatus.Completed).Should().BeFalse();
        BookingStatusMachine.CanTransition(BookingStatus.Confirmed, BookingStatus.Seated).Should().BeTrue();
        BookingStatusMachine.CanTransition(BookingStatus.Seated, BookingStatus.Confirmed).Should().BeFalse();
    }

    [Fact]
    public void Reference_uses_the_business_date_and_a_zero_padded_sequence()
    {
        BookingReference.Format(new LocalDate(2026, 10, 9), 42).Should().Be("BK-20261009-000042");
        BookingReference.Format(new LocalDate(2026, 1, 2), 1234567).Should().Be("BK-20260102-1234567");
        FluentActions.Invoking(() => BookingReference.Format(new LocalDate(2026, 1, 2), 0)).Should().Throw<ArgumentOutOfRangeException>();
    }
}

public sealed class BookingTimingTests
{
    private static readonly DateTimeZone Johannesburg = TimeZones.Get("Africa/Johannesburg");
    private static readonly DateOnly Friday = new(2026, 10, 9);
    private static readonly VenueBookingRules Rules = VenueBookingRules.Default with { TurnoverBufferMinutes = 15 };

    private static OperatingSchedule Schedule(params WeeklyPeriod[] periods) => new(Johannesburg, periods, []);

    private static Instant Utc(int day, int hour, int minute = 0) => Instant.FromUtc(2026, 10, day, hour, minute);

    [Fact]
    public void Resolves_a_window_inside_a_period_and_adds_turnover_to_the_claim()
    {
        var schedule = Schedule(new WeeklyPeriod(IsoDayOfWeek.Friday, new LocalTime(17, 0), new LocalTime(23, 0)));

        var timing = BookingAllocator.ResolveTiming(schedule, Rules, Friday, new TimeOnly(19, 30), 90);

        timing.Start.Should().Be(Utc(9, 17, 30));
        timing.End.Should().Be(Utc(9, 19, 0));
        timing.ClaimEnd.Should().Be(Utc(9, 19, 15));
        timing.BusinessDateOnly.Should().Be(Friday);
        timing.DurationMinutes.Should().Be(90);
    }

    [Fact]
    public void After_midnight_times_belong_to_the_overnight_period_of_the_business_date()
    {
        var schedule = Schedule(new WeeklyPeriod(IsoDayOfWeek.Friday, new LocalTime(20, 0), new LocalTime(3, 0)));

        var timing = BookingAllocator.ResolveTiming(schedule, Rules, Friday, new TimeOnly(1, 0), 60);

        timing.Start.Should().Be(Utc(9, 23, 0), "Saturday 01:00 local is Friday 23:00 UTC");
        timing.BusinessDateOnly.Should().Be(Friday);
    }

    [Fact]
    public void Rejects_windows_outside_hours_closed_days_and_out_of_range_durations()
    {
        var schedule = Schedule(new WeeklyPeriod(IsoDayOfWeek.Friday, new LocalTime(17, 0), new LocalTime(23, 0)));

        FluentActions.Invoking(() => BookingAllocator.ResolveTiming(schedule, Rules, Friday, new TimeOnly(22, 0), 120))
            .Should().Throw<BusinessRuleAppException>().Which.Code.Should().Be(ErrorCodes.OutsideOperatingHours);
        FluentActions.Invoking(() => BookingAllocator.ResolveTiming(schedule, Rules, Friday.AddDays(1), new TimeOnly(19, 0), 120))
            .Should().Throw<BusinessRuleAppException>().Which.Code.Should().Be(ErrorCodes.OutsideOperatingHours);
        FluentActions.Invoking(() => BookingAllocator.ResolveTiming(schedule, Rules, Friday, new TimeOnly(19, 0), 10))
            .Should().Throw<ValidationAppException>().Which.Errors.Should().ContainKey("durationMinutes");
    }

    [Fact]
    public void Defaults_to_the_venue_duration()
    {
        var schedule = Schedule(new WeeklyPeriod(IsoDayOfWeek.Friday, new LocalTime(12, 0), new LocalTime(23, 0)));
        BookingAllocator.ResolveTiming(schedule, Rules, Friday, new TimeOnly(18, 0), null).DurationMinutes.Should().Be(Rules.DefaultDurationMinutes);
    }
}

public sealed class BookingPrimitivesTests
{
    [Fact]
    public void Idempotency_request_requires_a_printable_key_and_hashes_method_route_and_body()
    {
        FluentActions.Invoking(() => IdempotencyRequest.Create(null, "POST", "/x", "{}"))
            .Should().Throw<BadRequestAppException>().Which.Code.Should().Be(ErrorCodes.IdempotencyKeyRequired);
        FluentActions.Invoking(() => IdempotencyRequest.Create(new string('k', 101), "POST", "/x", "{}")).Should().Throw<ValidationAppException>();
        FluentActions.Invoking(() => IdempotencyRequest.Create("a\nb", "POST", "/x", "{}")).Should().Throw<ValidationAppException>();

        var a = IdempotencyRequest.Create(" key-1 ", "post", "/api/v1/bookings", """{"a":1}""");
        var b = IdempotencyRequest.Create("key-1", "POST", "/api/v1/bookings", """{"a":1}""");
        var c = IdempotencyRequest.Create("key-1", "POST", "/api/v1/bookings", """{"a":2}""");
        a.Key.Should().Be("key-1");
        a.RequestHash.Should().HaveCount(32).And.Equal(b.RequestHash);
        c.RequestHash.Should().NotEqual(a.RequestHash);
    }

    [Theory]
    [InlineData("+27 82 555 0101", "+27825550101")]
    [InlineData("(021) 555-0101", "0215550101")]
    [InlineData("12", null)]
    [InlineData("call me", null)]
    [InlineData(null, null)]
    public void Phone_numbers_normalise_to_digits_with_an_optional_plus(string? input, string? expected) =>
        BookingService.NormalizePhone(input).Should().Be(expected);

    [Fact]
    public void Hold_tokens_are_random_and_only_their_hash_is_kept()
    {
        var (token, hash) = HoldTokens.Generate();
        var (other, _) = HoldTokens.Generate();
        token.Should().HaveLength(43).And.NotBe(other).And.MatchRegex("^[A-Za-z0-9_-]+$");
        HoldTokens.Hash(token).Should().Equal(hash).And.HaveCount(32);
    }

    [Fact]
    public void Options_at_an_instant_rank_free_singles_before_combinations()
    {
        var t1 = new AvailabilityResource(Guid.NewGuid(), "T1", 1, 4, 1, null);
        var t2 = new AvailabilityResource(Guid.NewGuid(), "T2", 1, 4, 2, null);
        var combo = new AvailabilityCombination(Guid.NewGuid(), "T1 + T2", 2, 8, 0, [new CombinationMember(t1.Id, true), new CombinationMember(t2.Id, true)]);
        var start = Instant.FromUtc(2026, 10, 9, 17, 0);
        var end = start + Duration.FromHours(2);

        var options = AvailabilityCalculator.OptionsAt(start, end, 2, [t1, t2], [combo], [new ResourceClaim(t1.Id, end, end + Duration.FromHours(1))]);
        options.Select(o => o.Id).Should().Equal(t1.Id, t2.Id, combo.Id);

        var busy = AvailabilityCalculator.OptionsAt(start, end, 2, [t1, t2], [combo], [new ResourceClaim(t1.Id, start, end)]);
        busy.Select(o => o.Id).Should().Equal(t2.Id);

        FluentActions.Invoking(() => AvailabilityCalculator.OptionsAt(start, start, 2, [t1], [], [])).Should().Throw<ArgumentOutOfRangeException>();
    }
}

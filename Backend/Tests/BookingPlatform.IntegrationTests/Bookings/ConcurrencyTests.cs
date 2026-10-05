using System.Net;
using BookingPlatform.IntegrationTests.Infrastructure;
using static BookingPlatform.IntegrationTests.Bookings.BookingScenario;

namespace BookingPlatform.IntegrationTests.Bookings;

/// <summary>
/// TESTING.md §5. Requests are released together through a gate; the exclusion constraint must let exactly the right
/// number succeed. Constraints are never weakened to make these pass.
/// </summary>
public sealed class ConcurrencyTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    private Task<BookingScenario> SetupAsync() => BookingScenario.CreateAsync(Data, SignInAsync);

    private static async Task<HttpResponseMessage[]> RunTogetherAsync(IEnumerable<Func<Task<HttpResponseMessage>>> requests)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = requests.Select(async send =>
        {
            await gate.Task;
            return await send();
        }).ToList();
        gate.SetResult();
        return await Task.WhenAll(tasks);
    }

    private static async Task ShouldAllBeNotAvailableAsync(IEnumerable<HttpResponseMessage> responses)
    {
        foreach (var response in responses)
        {
            await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE");
        }
    }

    [Fact]
    public async Task One_hundred_simultaneous_requests_for_one_resource_produce_exactly_one_booking()
    {
        var s = await SetupAsync();

        var responses = await RunTogetherAsync(Enumerable.Range(0, 100)
            .Select(_ => (Func<Task<HttpResponseMessage>>)(() => s.CreateBookingAsync(s.Booking("19:00", 2, Resources(s.T1))))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        await ShouldAllBeNotAvailableAsync(responses.Where(r => r.StatusCode != HttpStatusCode.Created));
        (await Data.ScalarAsync<long>("SELECT count(*) FROM bookings")).Should().Be(1);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM booking_allocations WHERE is_active")).Should().Be(1);
        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Combination_and_member_requests_race_to_exactly_one_success_without_partial_allocations()
    {
        var s = await SetupAsync();

        var combination = Enumerable.Range(0, 50).Select(_ => (Func<Task<HttpResponseMessage>>)(() => s.CreateBookingAsync(s.Booking("19:00", 6, s.Combination()))));
        var member = Enumerable.Range(0, 50).Select(_ => (Func<Task<HttpResponseMessage>>)(() => s.CreateBookingAsync(s.Booking("19:00", 2, Resources(s.T3)))));
        var responses = await RunTogetherAsync(combination.Concat(member).OrderBy(_ => Random.Shared.Next()));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        await ShouldAllBeNotAvailableAsync(responses.Where(r => r.StatusCode != HttpStatusCode.Created));
        var bookingCount = await Data.ScalarAsync<long>("SELECT count(*) FROM bookings");
        bookingCount.Should().Be(1);
        var allocations = await Data.ScalarAsync<long>("SELECT count(*) FROM booking_allocations WHERE is_active");
        allocations.Should().BeOneOf([1L, 2L], "either T3 alone or both members of the combination, never a partial combination");
        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Overlapping_windows_on_one_resource_yield_one_success_and_adjacent_windows_both_succeed()
    {
        var s = await SetupAsync();

        var early = Enumerable.Range(0, 20).Select(_ => (Func<Task<HttpResponseMessage>>)(() => s.CreateBookingAsync(s.Booking("19:00", 2, Resources(s.T1)))));
        var late = Enumerable.Range(0, 20).Select(_ => (Func<Task<HttpResponseMessage>>)(() => s.CreateBookingAsync(s.Booking("20:00", 2, Resources(s.T1)))));
        var overlapping = await RunTogetherAsync(early.Concat(late));
        overlapping.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        await ShouldAllBeNotAvailableAsync(overlapping.Where(r => r.StatusCode != HttpStatusCode.Created));

        var adjacent = await RunTogetherAsync(
        [
            () => s.CreateBookingAsync(s.Booking("17:00", 2, Resources(s.T2))),
            () => s.CreateBookingAsync(s.Booking("19:00", 2, Resources(s.T2))),
            () => s.CreateBookingAsync(s.Booking("21:00", 2, Resources(s.T2))),
        ]);
        adjacent.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created, "[17,19), [19,21) and [21,23) only touch");
        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Concurrent_auto_assignment_fills_each_suitable_table_exactly_once()
    {
        var s = await SetupAsync();

        var responses = await RunTogetherAsync(Enumerable.Range(0, 10)
            .Select(_ => (Func<Task<HttpResponseMessage>>)(() => s.CreateBookingAsync(s.Booking("19:00", 2)))));

        var created = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        var failures = await Task.WhenAll(responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}"));
        created.Should().HaveCount(3, "three tables can seat a party of two; failures: {0}", string.Join(" | ", failures));
        await ShouldAllBeNotAvailableAsync(responses.Where(r => r.StatusCode != HttpStatusCode.Created));
        var tables = await Task.WhenAll(created.Select(async r => ResourceIdsOf(await r.JsonAsync()).Single()));
        tables.Should().BeEquivalentTo([s.T1, s.T2, s.T3]);
        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Concurrent_move_and_create_for_the_same_slot_never_leave_a_booking_unallocated()
    {
        var s = await SetupAsync();
        for (var round = 0; round < 5; round++)
        {
            var time = $"{17 + round % 3}:{(round / 3) * 30:00}";
            var existing = await s.CreateBookingOkAsync(s.Booking(time, 2, Resources(s.T1)));
            var existingId = existing.GetProperty("id").GetGuid();
            var target = s.T2;

            var responses = await RunTogetherAsync(
            [
                () => s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{existingId}/move", new { allocation = Resources(target) }),
                () => s.CreateBookingAsync(s.Booking(time, 2, Resources(target))),
            ]);

            responses.Count(r => r.IsSuccessStatusCode).Should().Be(1, $"round {round}: move and create target the same slot");
            await ShouldAllBeNotAvailableAsync(responses.Where(r => !r.IsSuccessStatusCode));
            (await Data.ScalarAsync<long>("SELECT count(*) FROM booking_allocations WHERE booking_id = @existingId AND is_active", new { existingId }))
                .Should().Be(1, "the moved booking keeps exactly one active allocation whichever request wins");
            await BookingInvariants.AssertAsync(Data);

            await Data.ExecuteAsync(
                """
                UPDATE booking_allocations SET is_active = false, released_at = now(), release_reason = 'CANCELLED' WHERE is_active;
                UPDATE bookings SET status = 'CANCELLED', cancelled_at = now() WHERE status <> 'CANCELLED';
                """);
        }
    }
}

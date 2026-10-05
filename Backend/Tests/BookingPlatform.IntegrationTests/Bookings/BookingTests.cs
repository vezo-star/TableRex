using System.Net;
using System.Text.Json;
using BookingPlatform.IntegrationTests.Infrastructure;
using static BookingPlatform.IntegrationTests.Bookings.BookingScenario;

namespace BookingPlatform.IntegrationTests.Bookings;

public sealed class BookingTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    private Task<BookingScenario> SetupAsync() => BookingScenario.CreateAsync(Data, SignInAsync);

    private Task<long> CountAsync(string sql, object? parameters = null) => Data.ScalarAsync<long>(sql, parameters);

    [Fact]
    public async Task Create_writes_the_booking_and_every_side_table_in_one_transaction()
    {
        var s = await SetupAsync();

        var booking = await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T1)));
        var id = booking.GetProperty("id").GetGuid();

        booking.GetProperty("reference").GetString().Should().MatchRegex($"^BK-{Date:yyyyMMdd}-[0-9]{{6,}}$");
        booking.GetProperty("status").GetString().Should().Be("CONFIRMED");
        booking.GetProperty("source").GetString().Should().Be("STAFF");
        booking.GetProperty("time").GetString().Should().Be("19:00");
        booking.GetProperty("durationMinutes").GetInt32().Should().Be(120);
        booking.GetProperty("startsAt").GetDateTime().Should().Be(Local(19));
        booking.GetProperty("businessDate").GetString().Should().Be(DateText);
        booking.GetProperty("version").GetInt32().Should().Be(1);
        booking.GetProperty("customer").GetProperty("firstName").GetString().Should().Be("Ada");
        ResourceIdsOf(booking).Should().Equal(s.T1);

        (await CountAsync("SELECT count(*) FROM booking_allocations WHERE booking_id = @id AND is_active AND ends_at = @end", new { id, end = Local(21) }))
            .Should().Be(1);
        (await CountAsync("SELECT count(*) FROM booking_status_history WHERE booking_id = @id AND from_status IS NULL AND to_status = 'CONFIRMED'", new { id }))
            .Should().Be(1);
        (await CountAsync("SELECT count(*) FROM booking_events WHERE booking_id = @id AND event_type = 'CREATED'", new { id })).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM audit_logs WHERE entity_type = 'booking' AND entity_id = @id AND action = 'created'", new { id }))
            .Should().Be(1);
        (await CountAsync("SELECT count(*) FROM outbox_messages WHERE payload->>'entityId' = @id AND type = 'BookingCreated' AND destination = 'REALTIME'",
            new { id = id.ToString() })).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM outbox_messages WHERE payload->>'bookingId' = @id AND destination = 'NOTIFICATION'", new { id = id.ToString() }))
            .Should().Be(1);
        (await Data.ScalarAsync<string>("SELECT payload::text FROM outbox_messages WHERE type = 'BookingCreated'"))
            .Should().NotContain("Ada").And.NotContain("example.test", "realtime payloads carry no PII");
        (await Data.ScalarAsync<string>("SELECT new_values::text FROM audit_logs WHERE entity_type = 'booking'"))
            .Should().NotContain("example.test").And.NotContain("Regular", "audit snapshots exclude contact details and notes");

        var fetched = await (await s.Session.GetAsync($"/api/v1/bookings/{id}")).JsonAsync();
        fetched.GetProperty("reference").GetString().Should().Be(booking.GetProperty("reference").GetString());

        var list = await (await s.Session.GetAsync($"/api/v1/bookings?venueId={s.VenueId}&date={DateText}&status=CONFIRMED,SEATED&q=lovelace")).JsonAsync();
        list.GetProperty("totalCount").GetInt64().Should().Be(1);
        list.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(id);

        var history = await (await s.Session.GetAsync($"/api/v1/bookings/{id}/history")).JsonAsync();
        history.GetProperty("statusHistory").GetArrayLength().Should().Be(1);
        history.GetProperty("events")[0].GetProperty("data").GetProperty("reference").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Idempotency_replays_the_original_response_and_rejects_a_different_payload()
    {
        var s = await SetupAsync();
        var body = s.Booking("19:00", 2, Resources(s.T1));
        var key = Guid.NewGuid().ToString();

        var first = await s.CreateBookingAsync(body, key);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(Ct));
        first.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        var firstBody = await first.Content.ReadAsStringAsync(Ct);

        var replay = await s.CreateBookingAsync(body, key);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        (await replay.Content.ReadAsStringAsync(Ct)).Should().Be(firstBody);
        (await CountAsync("SELECT count(*) FROM bookings")).Should().Be(1);

        var different = await s.CreateBookingAsync(s.Booking("20:00", 2, Resources(s.T2)), key);
        await different.ShouldBeProblemAsync(HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");

        var missing = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/bookings", body);
        await missing.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "IDEMPOTENCY_KEY_REQUIRED");

        var tooLong = await s.CreateBookingAsync(body, new string('k', 101));
        await tooLong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var concurrentKey = Guid.NewGuid().ToString();
        var concurrentBody = s.Booking("21:00", 2, Resources(s.T3));
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => s.CreateBookingAsync(concurrentBody, concurrentKey)));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var ids = await Task.WhenAll(responses.Select(async r => (await r.JsonAsync()).GetProperty("id").GetGuid()));
        ids.Distinct().Should().ContainSingle("ten concurrent requests with one key create exactly one booking");
        (await CountAsync("SELECT count(*) FROM bookings")).Should().Be(2);
    }

    [Fact]
    public async Task Conflicts_return_409_with_resource_ids_and_write_nothing_while_adjacent_windows_succeed()
    {
        var s = await SetupAsync();
        await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T1)));

        var overlap = await s.CreateBookingAsync(s.Booking("20:00", 2, Resources(s.T1)));
        await overlap.ShouldBeProblemAsync(HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE");
        var problem = await overlap.JsonAsync();
        problem.GetProperty("conflictingResourceIds").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(s.T1);

        var invalid = await s.CreateBookingAsync(s.Booking("20:00", 0, Resources(s.T2)));
        await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        (await CountAsync("SELECT count(*) FROM bookings")).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM customers")).Should().Be(1, "the failed requests rolled back their new customers");
        (await CountAsync("SELECT count(*) FROM idempotency_keys")).Should().Be(1, "failed requests roll back their key so a retry re-executes");
        (await CountAsync("SELECT count(*) FROM outbox_messages WHERE destination = 'REALTIME' AND type LIKE 'Booking%'")).Should().Be(1);

        var adjacent = await s.CreateBookingAsync(s.Booking("21:00", 2, Resources(s.T1)));
        adjacent.StatusCode.Should().Be(HttpStatusCode.Created, "[19:00, 21:00) and [21:00, 23:00) only touch");

        var outside = await s.CreateBookingAsync(s.Booking("22:00", 2, Resources(s.T2)));
        await outside.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "OUTSIDE_OPERATING_HOURS");

        var tooBig = await s.CreateBookingAsync(s.Booking("17:00", 9, Resources(s.T2)));
        await tooBig.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PARTY_SIZE_NOT_SUPPORTED");

        var pastBody = With(s.Booking("19:00", 2, Resources(s.T2)), "date", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)).ToString("yyyy-MM-dd"));
        var inPast = await s.CreateBookingAsync(pastBody);
        await inPast.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "INVALID_BOOKING_TIME");

        await Data.ExecuteAsync("UPDATE venues SET turnover_buffer_minutes = 15 WHERE id = @id", new { id = s.VenueId });
        var withTurnover = await s.CreateBookingOkAsync(s.Booking("17:00", 2, Resources(s.T3)));
        (await Data.ScalarAsync<DateTime>("SELECT ends_at FROM booking_allocations WHERE booking_id = @id",
            new { id = withTurnover.GetProperty("id").GetGuid() })).Should().Be(Local(19, 15));
        withTurnover.GetProperty("endsAt").GetDateTime().Should().Be(Local(19), "the guest-facing end excludes turnover");

        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Auto_assignment_and_combinations_allocate_the_best_fit()
    {
        var s = await SetupAsync();

        var auto = await s.CreateBookingOkAsync(s.Booking("19:00", 2));
        ResourceIdsOf(auto).Should().Equal(s.T1);
        var second = await s.CreateBookingOkAsync(s.Booking("19:00", 2));
        ResourceIdsOf(second).Should().Equal(s.T2);

        var large = await s.CreateBookingOkAsync(s.Booking("17:00", 6, s.Combination()));
        ResourceIdsOf(large).Should().BeEquivalentTo([s.T2, s.T3]);
        large.GetProperty("resources").EnumerateArray().Should().OnlyContain(r => r.GetProperty("combinationId").GetGuid() == s.CombinationId);
        (await CountAsync("SELECT count(*) FROM booking_allocations WHERE booking_id = @id AND is_active", new { id = large.GetProperty("id").GetGuid() }))
            .Should().Be(2);

        var autoLarge = await s.CreateBookingAsync(s.Booking("19:00", 6));
        await autoLarge.ShouldBeProblemAsync(HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE");

        var noTable = await s.CreateBookingAsync(s.Booking("19:00", 20));
        await noTable.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PARTY_SIZE_NOT_SUPPORTED");

        var walkIn = await s.CreateBookingOkAsync(WithoutCustomer(s.Booking("21:00", 2, source: "WALK_IN")));
        walkIn.GetProperty("status").GetString().Should().Be("SEATED");
        walkIn.GetProperty("customer").ValueKind.Should().Be(JsonValueKind.Null);

        await BookingInvariants.AssertAsync(Data);
    }

    private static Dictionary<string, object?> WithoutCustomer(object body) => With(body, "customer", null);

    /// <summary>The body as a JSON dictionary with one property replaced.</summary>
    private static Dictionary<string, object?> With(object body, string property, object? value)
    {
        var element = JsonSerializer.SerializeToElement(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var dict = element.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        dict[property] = value;
        return dict;
    }

    [Fact]
    public async Task Status_commands_follow_the_state_machine_and_release_resources()
    {
        var s = await SetupAsync();
        var booking = await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T1)));
        var id = booking.GetProperty("id").GetGuid();

        var confirmAgain = await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/confirm");
        await confirmAgain.ShouldBeProblemAsync(HttpStatusCode.Conflict, "INVALID_STATUS_TRANSITION");

        var stale = await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/check-in", new { version = 7 });
        await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT");

        var noShowEarly = await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/no-show");
        await noShowEarly.ShouldBeProblemAsync(HttpStatusCode.Conflict, "INVALID_STATUS_TRANSITION");

        var checkedIn = await (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/check-in", new { version = 1 })).JsonAsync();
        checkedIn.GetProperty("status").GetString().Should().Be("CHECKED_IN");
        checkedIn.GetProperty("version").GetInt32().Should().Be(2);
        var seated = await (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/seat")).JsonAsync();
        seated.GetProperty("status").GetString().Should().Be("SEATED");
        var cancelSeated = await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/cancel", new { reason = "x" });
        await cancelSeated.ShouldBeProblemAsync(HttpStatusCode.Conflict, "INVALID_STATUS_TRANSITION");

        var completed = await (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/complete")).JsonAsync();
        completed.GetProperty("status").GetString().Should().Be("COMPLETED");
        completed.GetProperty("completedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        (await Data.ScalarAsync<DateTime>("SELECT ends_at FROM booking_allocations WHERE booking_id = @id AND is_active", new { id }))
            .Should().Be(Local(19, 1), "completing early truncates the claim so the table frees up");
        (await s.CreateBookingAsync(s.Booking("19:15", 2, Resources(s.T1)))).StatusCode.Should().Be(HttpStatusCode.Created);

        var history = await (await s.Session.GetAsync($"/api/v1/bookings/{id}/history")).JsonAsync();
        history.GetProperty("statusHistory").EnumerateArray().Select(h => h.GetProperty("toStatus").GetString())
            .Should().Equal("CONFIRMED", "CHECKED_IN", "SEATED", "COMPLETED");

        var toCancel = await s.CreateBookingOkAsync(s.Booking("17:00", 2, Resources(s.T2)));
        var cancelId = toCancel.GetProperty("id").GetGuid();
        var cancelled = await (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{cancelId}/cancel", new { reason = "Guest called" })).JsonAsync();
        cancelled.GetProperty("status").GetString().Should().Be("CANCELLED");
        cancelled.GetProperty("cancellationReason").GetString().Should().Be("Guest called");
        (await CountAsync("SELECT count(*) FROM booking_allocations WHERE booking_id = @cancelId AND NOT is_active AND release_reason = 'CANCELLED'", new { cancelId }))
            .Should().Be(1, "allocations are deactivated, never deleted");
        var again = await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{cancelId}/cancel");
        await again.ShouldBeProblemAsync(HttpStatusCode.Conflict, "BOOKING_ALREADY_CANCELLED");
        (await s.CreateBookingAsync(s.Booking("17:00", 2, Resources(s.T2)))).StatusCode.Should().Be(HttpStatusCode.Created, "a cancelled booking frees the resource");
        (await CountAsync("SELECT count(*) FROM outbox_messages WHERE type = 'BookingCancelled'")).Should().Be(1);

        var noShow = await s.CreateBookingOkAsync(s.Booking("21:00", 2, Resources(s.T3)));
        var noShowId = noShow.GetProperty("id").GetGuid();
        await Data.ExecuteAsync(
            """
            UPDATE booking_allocations SET starts_at = starts_at - interval '20 days', ends_at = ends_at - interval '20 days' WHERE booking_id = @noShowId;
            UPDATE bookings SET starts_at = starts_at - interval '20 days', ends_at = ends_at - interval '20 days' WHERE id = @noShowId;
            """,
            new { noShowId });
        var markedNoShow = await (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{noShowId}/no-show")).JsonAsync();
        markedNoShow.GetProperty("status").GetString().Should().Be("NO_SHOW");
        (await CountAsync("SELECT count(*) FROM booking_allocations WHERE booking_id = @noShowId AND is_active", new { noShowId })).Should().Be(0);

        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Update_and_move_are_atomic_and_versioned()
    {
        var s = await SetupAsync();
        var booking = await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T1)));
        var id = booking.GetProperty("id").GetGuid();

        var updated = await (await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/bookings/{id}",
            new { partySize = 4, specialRequests = "Birthday", internalNotes = (string?)null, version = 1 })).JsonAsync();
        updated.GetProperty("partySize").GetInt32().Should().Be(4);
        updated.GetProperty("specialRequests").GetString().Should().Be("Birthday");
        updated.GetProperty("version").GetInt32().Should().Be(2);

        var tooMany = await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/bookings/{id}", new { partySize = 6, version = 2 });
        await tooMany.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PARTY_SIZE_NOT_SUPPORTED");
        var staleUpdate = await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/bookings/{id}", new { partySize = 3, version = 1 });
        await staleUpdate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT");

        var moved = await (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/move",
            new { time = "20:00", allocation = Resources(s.T2), version = 2 })).JsonAsync();
        moved.GetProperty("time").GetString().Should().Be("20:00");
        ResourceIdsOf(moved).Should().Equal(s.T2);
        (await CountAsync("SELECT count(*) FROM booking_allocations WHERE booking_id = @id AND NOT is_active AND release_reason = 'MOVED'", new { id }))
            .Should().Be(1);
        (await CountAsync("SELECT count(*) FROM booking_events WHERE booking_id = @id AND event_type = 'MOVED'", new { id })).Should().Be(1);

        var blocker = await s.CreateBookingOkAsync(s.Booking("17:00", 2, Resources(s.T3)));
        var intoConflict = await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/move", new { time = "17:30", allocation = Resources(s.T3) });
        await intoConflict.ShouldBeProblemAsync(HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE");
        var after = await (await s.Session.GetAsync($"/api/v1/bookings/{id}")).JsonAsync();
        after.GetProperty("time").GetString().Should().Be("20:00");
        ResourceIdsOf(after).Should().Equal(s.T2);
        (await CountAsync("SELECT count(*) FROM booking_allocations WHERE booking_id = @id AND is_active AND resource_id = @t2", new { id, t2 = s.T2 }))
            .Should().Be(1, "a failed move leaves the original allocation intact");
        blocker.GetProperty("id").GetGuid().Should().NotBeEmpty();

        var sameTableLater = await (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/move", new { time = "20:30" })).JsonAsync();
        ResourceIdsOf(sameTableLater).Should().Equal([s.T2], "omitting allocation keeps the current resources");

        var emptyMove = await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/move", new { });
        await emptyMove.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Holds_block_availability_convert_into_bookings_and_stop_blocking_when_expired()
    {
        var s = await SetupAsync();
        var holdBody = new { venueId = s.VenueId, date = DateText, time = "19:00", durationMinutes = 120, partySize = 2, allocation = Resources(s.T1) };
        var key = Guid.NewGuid().ToString();
        var holdResponse = await s.Session.PostIdempotentAsync("/api/v1/holds", holdBody, key);
        holdResponse.StatusCode.Should().Be(HttpStatusCode.Created, await holdResponse.Content.ReadAsStringAsync(Ct));
        var hold = await holdResponse.JsonAsync();
        var token = hold.GetProperty("token").GetString();
        token.Should().NotBeNullOrEmpty();
        (await CountAsync("SELECT count(*) FROM resource_holds WHERE token_hash = sha256(convert_to(@token, 'UTF8'))", new { token })).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM idempotency_keys WHERE response_body::text LIKE '%' || @token || '%'", new { token }))
            .Should().Be(0, "the plaintext token is never stored");

        var replay = await s.Session.PostIdempotentAsync("/api/v1/holds", holdBody, key);
        replay.Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        (await replay.JsonAsync()).GetProperty("token").ValueKind.Should().Be(JsonValueKind.Null);

        var blocked = await s.CreateBookingAsync(s.Booking("19:00", 2, Resources(s.T1)));
        await blocked.ShouldBeProblemAsync(HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE");

        var converted = await s.CreateBookingOkAsync(s.Booking("19:00", 2, holdToken: token, allocation: new { mode = "AUTO" }));
        ResourceIdsOf(converted).Should().Equal(s.T1);
        (await Data.ScalarAsync<string>("SELECT status FROM resource_holds WHERE id = @id", new { id = hold.GetProperty("id").GetGuid() }))
            .Should().Be("CONVERTED");
        (await CountAsync("SELECT count(*) FROM booking_allocations WHERE hold_id = @id AND release_reason = 'HOLD_CONVERTED'", new { id = hold.GetProperty("id").GetGuid() }))
            .Should().Be(1);
        var reuse = await s.CreateBookingAsync(s.Booking("19:00", 2, holdToken: token));
        await reuse.ShouldBeProblemAsync(HttpStatusCode.NotFound, "HOLD_NOT_FOUND");

        var second = await (await s.Session.PostIdempotentAsync("/api/v1/holds", holdBody with { allocation = Resources(s.T2) })).JsonAsync();
        var secondId = second.GetProperty("id").GetGuid();
        (await s.Session.SendAsync(HttpMethod.Delete, $"/api/v1/holds/{secondId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Data.ScalarAsync<string>("SELECT status FROM resource_holds WHERE id = @secondId", new { secondId })).Should().Be("RELEASED");
        (await s.Session.SendAsync(HttpMethod.Delete, $"/api/v1/holds/{secondId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var third = await (await s.Session.PostIdempotentAsync("/api/v1/holds", holdBody with { allocation = Resources(s.T3) })).JsonAsync();
        var thirdId = third.GetProperty("id").GetGuid();
        await Data.ExecuteAsync(
            """
            UPDATE resource_holds SET expires_at = now() - interval '1 minute' WHERE id = @thirdId;
            UPDATE booking_allocations SET expires_at = now() - interval '1 minute' WHERE hold_id = @thirdId;
            """,
            new { thirdId });
        var expiredToken = await s.CreateBookingAsync(s.Booking("19:00", 2, holdToken: third.GetProperty("token").GetString()));
        await expiredToken.ShouldBeProblemAsync(HttpStatusCode.Gone, "HOLD_EXPIRED");
        var overExpired = await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T3)));
        ResourceIdsOf(overExpired).Should().Equal(s.T3);
        (await Data.ScalarAsync<string>("SELECT status FROM resource_holds WHERE id = @thirdId", new { thirdId }))
            .Should().Be("EXPIRED", "the booking transaction released the expired hold lazily");

        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Blocks_prevent_bookings_until_cancelled()
    {
        var s = await SetupAsync();
        var create = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/resource-blocks", new
        {
            venueId = s.VenueId, resourceIds = new[] { s.T1 }, startsAt = Local(19), endsAt = Local(21), reason = "MAINTENANCE", notes = "Wobbly leg",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync(Ct));
        var block = await create.JsonAsync();
        var blockId = block.GetProperty("id").GetGuid();
        block.GetProperty("resourceIds").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(s.T1);

        await (await s.CreateBookingAsync(s.Booking("20:00", 2, Resources(s.T1)))).ShouldBeProblemAsync(HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE");

        await s.CreateBookingOkAsync(s.Booking("17:00", 2, Resources(s.T2)));
        var overBooking = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/resource-blocks", new
        {
            venueId = s.VenueId, resourceIds = new[] { s.T2 }, startsAt = Local(18), endsAt = Local(20), reason = "CLEANING",
        });
        await overBooking.ShouldBeProblemAsync(HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE");

        var badReason = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/resource-blocks", new
        {
            venueId = s.VenueId, resourceIds = new[] { s.T3 }, startsAt = Local(18), endsAt = Local(20), reason = "NAP",
        });
        await badReason.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var list = await (await s.Session.GetAsync($"/api/v1/resource-blocks?venueId={s.VenueId}&from={Local(17):O}&to={Local(23):O}")).JsonAsync();
        list.GetArrayLength().Should().Be(1);

        var cancelled = await (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/resource-blocks/{blockId}/cancel")).JsonAsync();
        cancelled.GetProperty("status").GetString().Should().Be("CANCELLED");
        (await s.CreateBookingAsync(s.Booking("20:00", 2, Resources(s.T1)))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await (await s.Session.GetAsync($"/api/v1/resource-blocks?venueId={s.VenueId}")).JsonAsync()).GetArrayLength().Should().Be(0);
        (await (await s.Session.GetAsync($"/api/v1/resource-blocks?venueId={s.VenueId}&includeCancelled=true")).JsonAsync()).GetArrayLength().Should().Be(1);

        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Tenant_isolation_venue_scoping_and_permissions_hold_for_bookings()
    {
        var s = await SetupAsync();
        var booking = await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T1)));
        var id = booking.GetProperty("id").GetGuid();
        var customerId = booking.GetProperty("customer").GetProperty("id").GetGuid();

        var other = await BookingScenario.CreateAsync(Data, SignInAsync);
        await (await other.Session.GetAsync($"/api/v1/bookings/{id}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "BOOKING_NOT_FOUND");
        await (await other.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/cancel")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "BOOKING_NOT_FOUND");
        (await Data.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @id", new { id })).Should().Be("CONFIRMED");
        await (await other.Session.GetAsync($"/api/v1/bookings?venueId={s.VenueId}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        (await (await other.Session.GetAsync("/api/v1/bookings")).JsonAsync()).GetProperty("totalCount").GetInt64().Should().Be(0);

        var foreignResource = await other.CreateBookingAsync(other.Booking("19:00", 2, Resources(s.T1)));
        await foreignResource.ShouldBeProblemAsync(HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        var foreignCustomer = await other.CreateBookingAsync(other.Booking("19:00", 2, Resources(other.T1), customer: new { customerId }));
        await foreignCustomer.ShouldBeProblemAsync(HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND");
        (await CountAsync("SELECT count(*) FROM bookings WHERE tenant_id = @tenantId", new { tenantId = other.TenantId })).Should().Be(0);

        var host = await Data.CreateUserAsync();
        await Data.AddMembershipAsync(s.TenantId, host.UserId, "HOST");
        var hostSession = await SignInAsync(host);
        await (await hostSession.GetAsync($"/api/v1/bookings/{id}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "BOOKING_NOT_FOUND");

        var viewer = await Data.CreateUserAsync();
        var viewerMembership = await Data.AddMembershipAsync(s.TenantId, viewer.UserId, "VIEW_ONLY");
        await Data.ExecuteAsync("INSERT INTO tenant_user_venues (tenant_id, tenant_user_id, venue_id) VALUES (@TenantId, @TenantUserId, @venueId)",
            new { viewerMembership.TenantId, viewerMembership.TenantUserId, venueId = s.VenueId });
        var viewerSession = await SignInAsync(viewer);
        (await viewerSession.GetAsync($"/api/v1/bookings/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var forbidden = await viewerSession.PostIdempotentAsync("/api/v1/bookings", s.Booking("21:00", 2, Resources(s.T2)));
        await forbidden.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await viewerSession.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/cancel")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}

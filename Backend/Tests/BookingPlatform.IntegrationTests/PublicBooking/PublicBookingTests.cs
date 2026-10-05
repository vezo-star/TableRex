using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookingPlatform.IntegrationTests.Bookings;
using BookingPlatform.IntegrationTests.Infrastructure;
using static BookingPlatform.IntegrationTests.Bookings.BookingScenario;

namespace BookingPlatform.IntegrationTests.PublicBooking;

/// <summary>
/// Phase 8: anonymous booking through the same engine as staff bookings. The scenario venue (BookingScenario) is open
/// 17:00-23:00 with tables T1-T3 (1-4 guests) and the combination T2 + T3; venue defaults apply (60 min lead time,
/// 90-day window, 1-12 guests, auto-confirm, 120 min cancellation cutoff).
/// </summary>
public sealed class PublicBookingTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    private const string TokenHeader = "X-Booking-Token";

    private static readonly string[] VenueFields =
    [
        "tenantSlug", "tenantName", "slug", "name", "description", "addressLine1", "addressLine2", "city", "region", "postalCode",
        "countryCode", "phone", "email", "timeZone", "minPartySize", "maxPartySize", "durationMinutes", "minLeadTimeMinutes", "today",
        "lastBookableDate", "cancellationCutoffMinutes", "autoConfirm", "deposit", "branding",
    ];

    private static readonly string[] ConfirmationFields =
        ["reference", "status", "venueName", "date", "time", "durationMinutes", "partySize", "timeZone", "startsAt", "manageToken", "payment"];

    private static readonly string[] BookingFields =
    [
        "reference", "status", "tenantSlug", "venueSlug", "venueName", "venuePhone", "venueEmail", "date", "time", "durationMinutes",
        "partySize", "timeZone", "startsAt", "guestFirstName", "specialRequests", "canCancel", "cancelUntil", "cancelledAt", "payment",
    ];

    // The A/B races send more public requests than the default per-IP limit allows from one test client.
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["RateLimiting:Policies:public-booking:PermitLimit"] = "1000",
    };

    [Fact]
    public async Task Guests_see_venue_details_and_bookable_times_without_internal_data()
    {
        var (s, path) = await SetupAsync();
        var guest = NewSession();

        var venueResponse = await guest.GetAsync($"/api/v1/public/{path}");
        venueResponse.StatusCode.Should().Be(HttpStatusCode.OK, await venueResponse.Content.ReadAsStringAsync(Ct));
        var venue = await venueResponse.JsonAsync();
        venue.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(VenueFields);
        venue.GetProperty("name").GetString().Should().Be("Bistro");
        venue.GetProperty("durationMinutes").GetInt32().Should().Be(120);
        venue.GetProperty("maxPartySize").GetInt32().Should().Be(12);
        ShouldNotLeak(venue, s);

        var availability = await (await guest.GetAsync($"/api/v1/public/{path}/availability?date={DateText}&partySize=2")).JsonAsync();
        availability.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo(["date", "timeZone", "partySize", "durationMinutes", "isClosed", "times"]);
        availability.GetProperty("isClosed").GetBoolean().Should().BeFalse();
        var times = availability.GetProperty("times").EnumerateArray().Select(t => t.GetProperty("time").GetString()).ToList();
        times.Should().StartWith("17:00").And.Contain("21:00").And.NotContain("21:15", "a two-hour booking must end by 23:00");
        availability.GetProperty("times")[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["time", "startsAt"]);
        ShouldNotLeak(availability, s);

        await Data.ExecuteAsync("UPDATE bookable_resources SET is_bookable_online = false WHERE venue_id = @VenueId", new { s.VenueId });
        var offline = await (await guest.GetAsync($"/api/v1/public/{path}/availability?date={DateText}&partySize=2")).JsonAsync();
        offline.GetProperty("times").GetArrayLength().Should().Be(0, "resources that are not bookable online are never offered");

        await (await guest.GetAsync($"/api/v1/public/{path}/availability?date={DateText}&partySize=13"))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await guest.GetAsync($"/api/v1/public/{path}/availability?date={Date.AddDays(120):yyyy-MM-dd}&partySize=2"))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await guest.GetAsync($"/api/v1/public/{path}/availability?date={Date.AddDays(-30):yyyy-MM-dd}&partySize=2"))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var tenantSlug = path.Split('/')[0];
        await (await guest.GetAsync($"/api/v1/public/{tenantSlug}/no-such-venue")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        await (await guest.GetAsync("/api/v1/public/Not_A_Slug/x")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");

        await Data.ExecuteAsync("UPDATE venues SET public_booking_enabled = false WHERE id = @VenueId", new { s.VenueId });
        await (await guest.GetAsync($"/api/v1/public/{path}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        await Data.ExecuteAsync("UPDATE venues SET public_booking_enabled = true, status = 'INACTIVE' WHERE id = @VenueId", new { s.VenueId });
        await (await guest.GetAsync($"/api/v1/public/{path}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        await Data.ExecuteAsync("UPDATE venues SET status = 'ACTIVE' WHERE id = @VenueId", new { s.VenueId });
        await Data.ExecuteAsync("UPDATE tenants SET status = 'SUSPENDED' WHERE id = @TenantId", new { s.TenantId });
        await (await guest.GetAsync($"/api/v1/public/{path}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
    }

    [Fact]
    public async Task Guests_book_through_the_engine_and_manage_the_booking_with_their_token()
    {
        var (s, path) = await SetupAsync();
        await Data.ExecuteAsync("UPDATE bookable_resources SET is_bookable_online = false WHERE id = @T1", new { s.T1 });
        var guest = NewSession();
        var key = Guid.NewGuid().ToString();
        var request = GuestBooking("19:00", 2, "grace@example.test", marketingConsent: true);

        var created = await guest.PostIdempotentAsync($"/api/v1/public/{path}/bookings", request, key);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        created.Headers.CacheControl!.NoStore.Should().BeTrue();
        var confirmation = await created.JsonAsync();
        confirmation.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(ConfirmationFields);
        confirmation.GetProperty("status").GetString().Should().Be("CONFIRMED");
        confirmation.GetProperty("time").GetString().Should().Be("19:00");
        confirmation.GetProperty("date").GetString().Should().Be(DateText);
        var reference = confirmation.GetProperty("reference").GetString()!;
        var token = confirmation.GetProperty("manageToken").GetString()!;
        token.Length.Should().BeGreaterThanOrEqualTo(43);
        ShouldNotLeak(confirmation, s);

        var row = (await Data.QueryAsync<(Guid Id, string Source, Guid? CreatedBy, byte[] TokenHash, bool Consent)>(
            """
            SELECT b.id, b.source, b.created_by_user_id, b.public_token_hash, c.marketing_consent
            FROM bookings b JOIN customers c ON c.id = b.customer_id WHERE b.reference = @reference
            """, new { reference })).Single();
        row.Source.Should().Be("PUBLIC");
        row.CreatedBy.Should().BeNull();
        row.TokenHash.Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token)), "only the token's hash is stored");
        row.Consent.Should().BeTrue();
        (await Data.ScalarAsync<long>("SELECT count(*) FROM idempotency_keys WHERE response_body::text LIKE @token", new { token = $"%{token}%" }))
            .Should().Be(0, "the stored replay never contains the token");
        (await Data.QueryAsync<Guid>("SELECT resource_id FROM booking_resources WHERE booking_id = @Id", new { row.Id }))
            .Should().Equal([s.T2], "T1 is not bookable online, so the engine picks the next best table");
        (await Data.ScalarAsync<string>("SELECT source FROM booking_status_history WHERE booking_id = @Id", new { row.Id })).Should().Be("PUBLIC");
        (await Data.QueryAsync<string>("SELECT destination || ':' || type FROM outbox_messages WHERE payload::text LIKE @id ORDER BY 1",
                new { id = $"%{row.Id}%" }))
            .Should().Equal(["NOTIFICATION:BookingConfirmation", "REALTIME:BookingCreated"]);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE entity_id = @Id AND action = 'created' AND user_id IS NULL", new { row.Id }))
            .Should().Be(1);

        var staffView = await (await s.Session.GetAsync($"/api/v1/bookings/{row.Id}")).JsonAsync();
        staffView.GetProperty("source").GetString().Should().Be("PUBLIC");
        staffView.GetProperty("customer").GetProperty("firstName").GetString().Should().Be("Grace");

        var replay = await guest.PostIdempotentAsync($"/api/v1/public/{path}/bookings", request, key);
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        replay.Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        var replayed = await replay.JsonAsync();
        replayed.GetProperty("reference").GetString().Should().Be(reference);
        replayed.GetProperty("manageToken").GetString().Should().Be(token, "the token is re-derived for a replay");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM bookings")).Should().Be(1);

        await (await guest.PostIdempotentAsync($"/api/v1/public/{path}/bookings", GuestBooking("20:00", 2, "grace@example.test"), key))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        await (await guest.SendAsync(HttpMethod.Post, $"/api/v1/public/{path}/bookings", request))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "IDEMPOTENCY_KEY_REQUIRED");

        var mine = await guest.SendAsync(HttpMethod.Get, "/api/v1/public/booking", headers: Token(token));
        mine.StatusCode.Should().Be(HttpStatusCode.OK, await mine.Content.ReadAsStringAsync(Ct));
        var view = await mine.JsonAsync();
        view.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(BookingFields);
        view.GetProperty("reference").GetString().Should().Be(reference);
        view.GetProperty("guestFirstName").GetString().Should().Be("Grace");
        view.GetProperty("canCancel").GetBoolean().Should().BeTrue();
        view.GetProperty("cancelUntil").GetDateTime().Should().Be(Local(17), "120 minutes before 19:00");
        ShouldNotLeak(view, s);

        await (await guest.SendAsync(HttpMethod.Get, "/api/v1/public/booking")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "BOOKING_NOT_FOUND");
        await (await guest.SendAsync(HttpMethod.Get, "/api/v1/public/booking", headers: Token(token[..^2] + "xx")))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "BOOKING_NOT_FOUND");

        var cancel = await guest.SendAsync(HttpMethod.Post, "/api/v1/public/booking/cancel", headers: Token(token));
        cancel.StatusCode.Should().Be(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync(Ct));
        var cancelled = await cancel.JsonAsync();
        cancelled.GetProperty("status").GetString().Should().Be("CANCELLED");
        cancelled.GetProperty("canCancel").GetBoolean().Should().BeFalse();
        cancelled.GetProperty("cancelledAt").ValueKind.Should().Be(JsonValueKind.String);
        var again = await guest.SendAsync(HttpMethod.Post, "/api/v1/public/booking/cancel", headers: Token(token));
        again.StatusCode.Should().Be(HttpStatusCode.OK, "cancelling twice is harmless");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM booking_allocations WHERE booking_id = @Id AND is_active", new { row.Id })).Should().Be(0);
        (await Data.ScalarAsync<string>(
                "SELECT source || ':' || reason FROM booking_status_history WHERE booking_id = @Id AND to_status = 'CANCELLED'", new { row.Id }))
            .Should().Be("PUBLIC:Cancelled by the guest online.");
        (await Data.ScalarAsync<long>(
                "SELECT count(*) FROM outbox_messages WHERE type = 'BookingCancellation' AND payload::text LIKE @id", new { id = $"%{row.Id}%" }))
            .Should().Be(1);
        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Public_requests_are_held_to_the_venue_rules()
    {
        var (s, path) = await SetupAsync();
        var guest = NewSession();
        Task<HttpResponseMessage> Book(object body) => guest.PostIdempotentAsync($"/api/v1/public/{path}/bookings", body);

        await (await Book(GuestBooking("19:07", 2))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await Book(GuestBooking("16:00", 2))).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "OUTSIDE_OPERATING_HOURS");
        await (await Book(GuestBooking("19:00", 13))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await Book(GuestBooking("19:00", 2) with { Website = "http://spam.example" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await Book(GuestBooking("19:00", 2) with { Email = "not-an-email" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await Book(GuestBooking("19:00", 2) with { Date = Date.AddDays(100) })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        await Data.ExecuteAsync("UPDATE venues SET min_lead_time_minutes = 43200 WHERE id = @VenueId", new { s.VenueId });
        await (await Book(GuestBooking("19:00", 2))).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "OUTSIDE_BOOKING_WINDOW");
        var tooSoon = await (await guest.GetAsync($"/api/v1/public/{path}/availability?date={DateText}&partySize=2")).JsonAsync();
        tooSoon.GetProperty("times").GetArrayLength().Should().Be(0, "every slot is inside the 30-day lead time");
        await Data.ExecuteAsync("UPDATE venues SET min_lead_time_minutes = 60 WHERE id = @VenueId", new { s.VenueId });

        foreach (var time in new[] { "17:00", "19:00", "21:00" })
        {
            (await Book(GuestBooking(time, 2, "regular@example.test"))).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await (await Book(GuestBooking("17:00", 2, "Regular@Example.test")))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "CONTACT_BOOKING_LIMIT");
        (await Data.ScalarAsync<long>("SELECT count(DISTINCT customer_id) FROM bookings")).Should().Be(1, "the same email reuses one customer");

        await Data.ExecuteAsync("UPDATE venues SET auto_confirm_public_bookings = false WHERE id = @VenueId", new { s.VenueId });
        var pending = await Book(GuestBooking("19:00", 2, "pending@example.test"));
        pending.StatusCode.Should().Be(HttpStatusCode.Created, await pending.Content.ReadAsStringAsync(Ct));
        (await pending.JsonAsync()).GetProperty("status").GetString().Should().Be("PENDING");
        await BookingInvariants.AssertAsync(Data);
    }

    [Fact]
    public async Task Guests_cannot_cancel_after_the_cutoff_or_once_the_booking_has_progressed()
    {
        var (s, path) = await SetupAsync();
        var guest = NewSession();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2).AddDays(1));
        var created = await guest.PostIdempotentAsync($"/api/v1/public/{path}/bookings", GuestBooking("20:00", 2) with { Date = tomorrow });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var token = (await created.JsonAsync()).GetProperty("manageToken").GetString()!;

        await Data.ExecuteAsync("UPDATE venues SET public_cancellation_cutoff_minutes = 10080 WHERE id = @VenueId", new { s.VenueId });
        var view = await (await guest.SendAsync(HttpMethod.Get, "/api/v1/public/booking", headers: Token(token))).JsonAsync();
        view.GetProperty("canCancel").GetBoolean().Should().BeFalse("the booking starts within the 7-day cutoff");
        view.GetProperty("cancelUntil").ValueKind.Should().Be(JsonValueKind.Null);
        await (await guest.SendAsync(HttpMethod.Post, "/api/v1/public/booking/cancel", headers: Token(token)))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "CANCELLATION_NOT_ALLOWED");

        await Data.ExecuteAsync("UPDATE venues SET public_cancellation_cutoff_minutes = 0 WHERE id = @VenueId", new { s.VenueId });
        var id = await Data.ScalarAsync<Guid>("SELECT id FROM bookings");
        (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{id}/check-in", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        await (await guest.SendAsync(HttpMethod.Post, "/api/v1/public/booking/cancel", headers: Token(token)))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "CANCELLATION_NOT_ALLOWED");
        (await Data.ScalarAsync<string>("SELECT status FROM bookings WHERE id = @id", new { id })).Should().Be("CHECKED_IN");
    }

    [Fact]
    public async Task Two_guests_racing_for_the_last_table_get_exactly_one_booking()
    {
        var (s, path) = await SetupAsync();
        await Data.ExecuteAsync("UPDATE bookable_resources SET is_bookable_online = (id = @T1) WHERE venue_id = @VenueId", new { s.T1, s.VenueId });
        await Data.ExecuteAsync("UPDATE resource_combinations SET is_bookable_online = false WHERE venue_id = @VenueId", new { s.VenueId });

        var responses = await RunTogetherAsync(
        [
            () => NewSession().PostIdempotentAsync($"/api/v1/public/{path}/bookings", GuestBooking("19:00", 2, "guest-a@example.test")),
            () => NewSession().PostIdempotentAsync($"/api/v1/public/{path}/bookings", GuestBooking("19:00", 2, "guest-b@example.test")),
        ]);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "only one online table exists for 19:00");
        await responses.Single(r => r.StatusCode != HttpStatusCode.Created).ShouldBeProblemAsync(HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE");

        var times = (await (await NewSession().GetAsync($"/api/v1/public/{path}/availability?date={DateText}&partySize=2")).JsonAsync())
            .GetProperty("times").EnumerateArray().Select(t => t.GetProperty("time").GetString()).ToList();
        times.Should().NotContain(["17:15", "19:00", "20:45"], "the booked table is busy 19:00-21:00").And.Contain(["17:00", "21:00"]);

        var crowd = await RunTogetherAsync(Enumerable.Range(0, 20).Select(i => (Func<Task<HttpResponseMessage>>)(() =>
            NewSession().PostIdempotentAsync($"/api/v1/public/{path}/bookings", GuestBooking("21:00", 2, $"crowd-{i}@example.test")))));
        crowd.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        foreach (var loser in crowd.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            await loser.ShouldBeProblemAsync(HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE");
        }

        var mixed = await RunTogetherAsync(
        [
            () => s.CreateBookingAsync(s.Booking("17:00", 2, Resources(s.T1))),
            () => NewSession().PostIdempotentAsync($"/api/v1/public/{path}/bookings", GuestBooking("17:00", 2, "walk-up@example.test")),
        ]);
        mixed.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "staff and guests compete for the same claim");

        (await Data.ScalarAsync<long>("SELECT count(*) FROM bookings")).Should().Be(3);
        await BookingInvariants.AssertAsync(Data);
    }

    private async Task<(BookingScenario Scenario, string Path)> SetupAsync()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        var path = await Data.ScalarAsync<string>(
            "SELECT t.slug || '/' || v.slug FROM venues v JOIN tenants t ON t.id = v.tenant_id WHERE v.id = @VenueId", new { s.VenueId });
        return (s, path);
    }

    private static GuestRequest GuestBooking(string time, int partySize, string? email = null, bool marketingConsent = false) =>
        new(Date, time, partySize, "Grace", "Hopper", email ?? $"guest-{Guid.NewGuid():N}@example.test", "+27 82 555 0199", "Birthday", marketingConsent);

    private static Dictionary<string, string> Token(string token) => new() { [TokenHeader] = token };

    /// <summary>Public payloads never carry tenant, venue, resource, combination or customer ids, nor staff-only fields.</summary>
    private static void ShouldNotLeak(JsonElement body, BookingScenario s)
    {
        var raw = body.GetRawText();
        foreach (var id in new[] { s.TenantId, s.VenueId, s.T1, s.T2, s.T3, s.CombinationId, s.TypeId })
        {
            raw.Should().NotContain(id.ToString());
        }

        raw.Should().NotContainAny("internalNotes", "resourceId", "customerId", "\"id\"", "createdByUserId", "+27 82");
    }

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

    private sealed record GuestRequest(
        DateOnly Date, string Time, int PartySize, string FirstName, string LastName, string Email, string Phone, string SpecialRequests,
        bool MarketingConsent, string? Website = null);
}

/// <summary>Per-IP limits on the public endpoints, with policy overrides from configuration.</summary>
public sealed class PublicBookingRateLimitTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["RateLimiting:Policies:public-read:PermitLimit"] = "3",
    };

    [Fact]
    public async Task Public_reads_are_rate_limited_per_client()
    {
        var guest = NewSession();
        for (var i = 0; i < 3; i++)
        {
            await (await guest.GetAsync("/api/v1/public/no-tenant/no-venue")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        }

        var limited = await guest.GetAsync("/api/v1/public/no-tenant/no-venue");
        await limited.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        limited.Headers.RetryAfter.Should().NotBeNull();
    }
}

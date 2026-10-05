using System.Net;
using System.Text.Json;
using BookingPlatform.IntegrationTests.Infrastructure;
using static BookingPlatform.IntegrationTests.Bookings.BookingScenario;

namespace BookingPlatform.IntegrationTests.Bookings;

/// <summary>Phase 6 APIs behind the staff dashboard: customers, the booking board and the audit log viewer.</summary>
public sealed class StaffExperienceTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    private Task<BookingScenario> SetupAsync() => BookingScenario.CreateAsync(Data, SignInAsync);

    private async Task<ApiSession> MemberAsync(BookingScenario s, string role, bool assignVenue = true)
    {
        var user = await Data.CreateUserAsync();
        var membership = await Data.AddMembershipAsync(s.TenantId, user.UserId, role);
        if (assignVenue)
        {
            await Data.ExecuteAsync("INSERT INTO tenant_user_venues (tenant_id, tenant_user_id, venue_id) VALUES (@TenantId, @TenantUserId, @venueId)",
                new { membership.TenantId, membership.TenantUserId, venueId = s.VenueId });
        }

        return await SignInAsync(user);
    }

    [Fact]
    public async Task Customers_can_be_created_searched_edited_and_their_bookings_listed()
    {
        var s = await SetupAsync();
        var created = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/customers",
            new { firstName = " Grace ", lastName = "Hopper", email = "Grace.Hopper@Example.test", phone = "+27 82 555 0123", notes = "Prefers booths" });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        created.Headers.Location.Should().NotBeNull();
        var customer = await created.JsonAsync();
        var id = customer.GetProperty("id").GetGuid();
        customer.GetProperty("firstName").GetString().Should().Be("Grace");
        customer.GetProperty("email").GetString().Should().Be("grace.hopper@example.test");
        customer.GetProperty("bookingCount").GetInt32().Should().Be(0);
        customer.GetProperty("isAnonymized").GetBoolean().Should().BeFalse();

        var duplicate = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/customers", new { firstName = "Other", email = "GRACE.HOPPER@example.test" });
        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "EMAIL_TAKEN");
        (await duplicate.JsonAsync()).GetProperty("customerId").GetGuid().Should().Be(id);

        var invalid = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/customers", new { firstName = "", email = "not-an-email", phone = "12" });
        await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var errors = (await invalid.JsonAsync()).GetProperty("errors");
        errors.TryGetProperty("firstName", out _).Should().BeTrue();
        errors.TryGetProperty("email", out _).Should().BeTrue();
        errors.TryGetProperty("phone", out _).Should().BeTrue();

        foreach (var q in new[] { "hopp", "GRACE", "0123", "example.test" })
        {
            var found = await (await s.Session.GetAsync($"/api/v1/customers?q={Uri.EscapeDataString(q)}")).JsonAsync();
            found.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().Contain(id, $"'{q}' should match");
        }

        (await (await s.Session.GetAsync("/api/v1/customers?q=%25")).JsonAsync()).GetProperty("totalCount").GetInt64()
            .Should().Be(0, "LIKE wildcards in the query match literally");

        var updated = await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/customers/{id}",
            new { firstName = "Grace", lastName = "Hopper", email = "grace@example.test", phone = "+27 82 555 0123", marketingConsent = true });
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        (await updated.JsonAsync()).GetProperty("notes").ValueKind.Should().Be(JsonValueKind.Null);

        var audit = await Data.QueryAsync<string>("SELECT new_values::text FROM audit_logs WHERE entity_type = 'customer' AND entity_id = @id ORDER BY created_at", new { id });
        audit.Should().HaveCount(2);
        audit[1].Should().Contain("email").And.Contain("notes").And.Contain("marketingConsent").And.NotContain("lastName");
        string.Join(" ", audit).Should().NotContain("example.test").And.NotContain("Hopper").And.NotContain("booths", "customer audit rows hold field names only");

        var early = await s.CreateBookingOkAsync(s.Booking("17:00", 2, Resources(s.T1), customer: new { customerId = id }));
        var late = await s.CreateBookingOkAsync(s.Booking("20:00", 2, Resources(s.T2), customer: new { customerId = id }));
        var bookings = await (await s.Session.GetAsync($"/api/v1/customers/{id}/bookings")).JsonAsync();
        bookings.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid())
            .Should().Equal([late.GetProperty("id").GetGuid(), early.GetProperty("id").GetGuid()], "newest first");
        var stats = await (await s.Session.GetAsync($"/api/v1/customers/{id}")).JsonAsync();
        stats.GetProperty("bookingCount").GetInt32().Should().Be(2);
        stats.GetProperty("lastBookingAt").GetDateTime().Should().Be(Local(20));

        await (await s.Session.GetAsync($"/api/v1/customers/{Guid.NewGuid()}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Anonymising_a_customer_removes_personal_data_and_blocks_reuse()
    {
        var s = await SetupAsync();
        var booking = await s.CreateBookingOkAsync(s.Booking("19:00", 2, Resources(s.T1)));
        var id = booking.GetProperty("customer").GetProperty("id").GetGuid();
        var bookingId = booking.GetProperty("id").GetGuid();
        var email = booking.GetProperty("customer").GetProperty("email").GetString()!;

        var anonymized = await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/customers/{id}/anonymize");
        anonymized.StatusCode.Should().Be(HttpStatusCode.OK, await anonymized.Content.ReadAsStringAsync(Ct));
        var body = await anonymized.JsonAsync();
        body.GetProperty("isAnonymized").GetBoolean().Should().BeTrue();
        body.GetProperty("firstName").GetString().Should().Be("Anonymised");
        body.GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("phone").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("bookingCount").GetInt32().Should().Be(1, "bookings are kept for reporting");

        var row = await Data.QueryAsync<(string? Email, string? Phone, string? PhoneNormalized)>(
            "SELECT email::text, phone, phone_normalized FROM customers WHERE id = @id", new { id });
        row.Single().Should().Be(((string?)null, (string?)null, (string?)null));
        var notes = await Data.QueryAsync<(string? SpecialRequests, string? InternalNotes)>(
            "SELECT special_requests, internal_notes FROM bookings WHERE id = @bookingId", new { bookingId });
        notes.Single().Should().Be(((string?)null, (string?)null), "free-text booking fields may contain personal data");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE entity_id = @id AND action = 'anonymized'", new { id })).Should().Be(1);

        (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/customers/{id}/anonymize")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE entity_id = @id AND action = 'anonymized'", new { id }))
            .Should().Be(1, "repeating the erasure is a no-op");

        var edit = await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/customers/{id}", new { firstName = "Back" });
        await edit.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "CUSTOMER_ANONYMIZED");
        var rebook = await s.CreateBookingAsync(s.Booking("21:00", 2, Resources(s.T2), customer: new { customerId = id }));
        await rebook.ShouldBeProblemAsync(HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND");

        var sameEmail = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/customers", new { firstName = "Ada", email });
        sameEmail.StatusCode.Should().Be(HttpStatusCode.Created, "an anonymised record no longer owns its email");
    }

    [Fact]
    public async Task Customer_endpoints_enforce_permissions_and_tenant_isolation()
    {
        var s = await SetupAsync();
        var customer = await (await s.Session.SendAsync(HttpMethod.Post, "/api/v1/customers", new { firstName = "Linus" })).JsonAsync();
        var id = customer.GetProperty("id").GetGuid();

        var other = await BookingScenario.CreateAsync(Data, SignInAsync);
        await (await other.Session.GetAsync($"/api/v1/customers/{id}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND");
        await (await other.Session.SendAsync(HttpMethod.Put, $"/api/v1/customers/{id}", new { firstName = "X" }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND");
        await (await other.Session.SendAsync(HttpMethod.Post, $"/api/v1/customers/{id}/anonymize")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND");
        (await (await other.Session.GetAsync("/api/v1/customers")).JsonAsync()).GetProperty("totalCount").GetInt64().Should().Be(0);
        (await Data.ScalarAsync<string>("SELECT first_name FROM customers WHERE id = @id", new { id })).Should().Be("Linus");

        var staff = await MemberAsync(s, "STAFF");
        (await staff.GetAsync($"/api/v1/customers/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        await (await staff.SendAsync(HttpMethod.Post, "/api/v1/customers", new { firstName = "No" })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await staff.SendAsync(HttpMethod.Post, $"/api/v1/customers/{id}/anonymize")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");

        var viewer = await MemberAsync(s, "VIEW_ONLY");
        await (await viewer.GetAsync("/api/v1/customers")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Board_returns_the_day_with_periods_bookings_of_every_status_and_blocks()
    {
        var s = await SetupAsync();
        var kept = await s.CreateBookingOkAsync(s.Booking("18:00", 2, Resources(s.T1)));
        var cancelled = await s.CreateBookingOkAsync(s.Booking("20:00", 2, Resources(s.T2)));
        var cancelledId = cancelled.GetProperty("id").GetGuid();
        (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{cancelledId}/cancel", new { reason = "Guest called" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var block = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/resource-blocks",
            new { venueId = s.VenueId, resourceIds = new[] { s.T3 }, startsAt = Local(17), endsAt = Local(19), reason = "MAINTENANCE" });
        block.StatusCode.Should().Be(HttpStatusCode.Created, await block.Content.ReadAsStringAsync(Ct));
        await s.CreateBookingOkAsync(new
        {
            venueId = s.VenueId,
            customer = new { firstName = "Next" },
            date = Date.AddDays(1).ToString("yyyy-MM-dd"),
            time = "19:00",
            partySize = 2,
            allocation = Resources(s.T1),
        });

        var board = await (await s.Session.GetAsync($"/api/v1/bookings/board?venueId={s.VenueId}&date={DateText}")).JsonAsync();
        board.GetProperty("timeZone").GetString().Should().Be("Africa/Johannesburg");
        board.GetProperty("isClosed").GetBoolean().Should().BeFalse();
        board.GetProperty("truncated").GetBoolean().Should().BeFalse();
        var period = board.GetProperty("periods").EnumerateArray().Single();
        period.GetProperty("startsAt").GetDateTime().Should().Be(Local(17));
        period.GetProperty("endsAt").GetDateTime().Should().Be(Local(23));
        board.GetProperty("slotIntervalMinutes").GetInt32().Should().BePositive();
        board.GetProperty("bookings").EnumerateArray().Select(b => b.GetProperty("id").GetGuid())
            .Should().Equal([kept.GetProperty("id").GetGuid(), cancelledId], "only the requested business date, every status, by start");
        board.GetProperty("bookings")[1].GetProperty("status").GetString().Should().Be("CANCELLED");
        board.GetProperty("blocks").EnumerateArray().Single().GetProperty("resourceIds")[0].GetGuid().Should().Be(s.T3);

        var closedDate = Date.AddDays(2).ToString("yyyy-MM-dd");
        (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/venues/{s.VenueId}/special-hours", new { date = closedDate, isClosed = true }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var closed = await (await s.Session.GetAsync($"/api/v1/bookings/board?venueId={s.VenueId}&date={closedDate}")).JsonAsync();
        closed.GetProperty("isClosed").GetBoolean().Should().BeTrue();
        closed.GetProperty("periods").GetArrayLength().Should().Be(0);

        var unassigned = await MemberAsync(s, "HOST", assignVenue: false);
        await (await unassigned.GetAsync($"/api/v1/bookings/board?venueId={s.VenueId}&date={DateText}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        var other = await BookingScenario.CreateAsync(Data, SignInAsync);
        await (await other.Session.GetAsync($"/api/v1/bookings/board?venueId={s.VenueId}&date={DateText}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        await (await s.Session.GetAsync($"/api/v1/bookings/board?venueId={s.VenueId}")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Audit_log_is_tenant_scoped_filterable_paged_and_restricted_to_settings_managers()
    {
        var s = await SetupAsync();
        for (var i = 0; i < 3; i++)
        {
            (await s.Session.SendAsync(HttpMethod.Post, "/api/v1/customers", new { firstName = $"Audit{i}" })).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var all = await (await s.Session.GetAsync("/api/v1/audit-logs?limit=100")).JsonAsync();
        var items = all.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty();
        items.Select(i => i.GetProperty("createdAt").GetDateTime()).Should().BeInDescendingOrder();
        items[0].GetProperty("userEmail").GetString().Should().NotBeNullOrEmpty();

        var first = await (await s.Session.GetAsync("/api/v1/audit-logs?entityType=customer&limit=2")).JsonAsync();
        first.GetProperty("items").GetArrayLength().Should().Be(2);
        var cursor = first.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrEmpty();
        var second = await (await s.Session.GetAsync($"/api/v1/audit-logs?entityType=customer&limit=2&before={Uri.EscapeDataString(cursor!)}")).JsonAsync();
        second.GetProperty("items").GetArrayLength().Should().Be(1);
        second.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
        var pagedIds = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("entityId").GetGuid()).ToList();
        pagedIds.Should().OnlyHaveUniqueItems().And.HaveCount(3);

        await (await s.Session.GetAsync("/api/v1/audit-logs?before=not-a-cursor")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await s.Session.GetAsync("/api/v1/audit-logs?limit=1000")).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var other = await BookingScenario.CreateAsync(Data, SignInAsync);
        var foreign = await (await other.Session.GetAsync("/api/v1/audit-logs?entityType=customer")).JsonAsync();
        foreign.GetProperty("items").GetArrayLength().Should().Be(0, "another tenant's audit trail is invisible");

        var manager = await MemberAsync(s, "MANAGER");
        await (await manager.GetAsync("/api/v1/audit-logs")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}

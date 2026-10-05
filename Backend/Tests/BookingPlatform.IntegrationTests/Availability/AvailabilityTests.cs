using System.Net;
using System.Text.Json;
using BookingPlatform.IntegrationTests.Infrastructure;

namespace BookingPlatform.IntegrationTests.Availability;

/// <summary>Venues are in Africa/Johannesburg (UTC+2, no DST) and open 17:00-23:00 every day.</summary>
public sealed class AvailabilityTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    private static readonly DateOnly Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

    /// <summary>UTC instant for a Johannesburg local time on <see cref="Date"/>.</summary>
    private static DateTime Local(int hour, int minute = 0) => Date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc).AddHours(-2);

    private sealed record Setup(Guid TenantId, ApiSession Session, Guid VenueId, Guid TypeId, Guid T1, Guid T2, Guid T3);

    private async Task<Setup> CreateVenueWithTablesAsync()
    {
        var (tenantId, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);
        var venueId = await PostIdAsync(session, "/api/v1/venues",
            new { name = "Bistro", slug = "bistro-" + Guid.NewGuid().ToString("N")[..8], timeZone = "Africa/Johannesburg" });
        var periods = Enumerable.Range(1, 7).Select(day => new { dayOfWeek = day, openTime = "17:00", closeTime = "23:00" });
        var hours = await session.SendAsync(HttpMethod.Put, $"/api/v1/venues/{venueId}/operating-hours", new { periods });
        hours.StatusCode.Should().Be(HttpStatusCode.OK, await hours.Content.ReadAsStringAsync(Ct));

        var typeId = await PostIdAsync(session, "/api/v1/resource-types", new { code = "TABLE", name = "Table" });
        var t1 = await CreateTableAsync(session, venueId, typeId, "T1", 1);
        var t2 = await CreateTableAsync(session, venueId, typeId, "T2", 2);
        var t3 = await CreateTableAsync(session, venueId, typeId, "T3", 3);
        return new Setup(tenantId, session, venueId, typeId, t1, t2, t3);
    }

    private static Task<Guid> CreateTableAsync(ApiSession session, Guid venueId, Guid typeId, string code, int sortOrder, Guid? sectionId = null) =>
        PostIdAsync(session, "/api/v1/resources",
            new { venueId, sectionId, resourceTypeId = typeId, name = code, code, minCapacity = 1, capacity = 4, sortOrder });

    private static async Task<Guid> PostIdAsync(ApiSession session, string path, object body)
    {
        var response = await session.SendAsync(HttpMethod.Post, path, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> SearchAsync(ApiSession session, Guid venueId, int partySize, string extra = "")
    {
        var response = await session.GetAsync($"/api/v1/availability?venueId={venueId}&date={Date:yyyy-MM-dd}&partySize={partySize}{extra}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.JsonAsync();
    }

    private static JsonElement Slot(JsonElement result, string time) =>
        result.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("time").GetString() == time);

    private static IReadOnlyList<Guid> Offered(JsonElement slot) =>
        slot.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task Bookings_active_holds_and_blocks_occupy_resources_but_expired_holds_and_cancelled_bookings_do_not()
    {
        var s = await CreateVenueWithTablesAsync();
        await Data.SeedBookingAsync(s.TenantId, s.VenueId, s.T1, Local(19), Local(21));
        await Data.SeedHoldAsync(s.TenantId, s.VenueId, s.T2, Local(19), Local(21), DateTime.UtcNow.AddMinutes(5));
        await Data.SeedHoldAsync(s.TenantId, s.VenueId, s.T3, Local(19), Local(21), DateTime.UtcNow.AddMinutes(-1));
        await Data.SeedBlockAsync(s.TenantId, s.VenueId, s.T3, Local(17), Local(18));
        await Data.SeedBookingAsync(s.TenantId, s.VenueId, s.T2, Local(21), Local(23), active: false);

        var result = await SearchAsync(s.Session, s.VenueId, 2);

        result.GetProperty("isClosed").GetBoolean().Should().BeFalse();
        result.GetProperty("durationMinutes").GetInt32().Should().Be(120);
        result.GetProperty("slots").GetArrayLength().Should().Be(17, "17:00 to 21:00 every 15 minutes");
        Offered(Slot(result, "17:00")).Should().Equal(s.T1, s.T2);
        Offered(Slot(result, "18:00")).Should().Equal(s.T3);
        Offered(Slot(result, "19:00")).Should().Equal(s.T3);
        Offered(Slot(result, "21:00")).Should().Equal(s.T1, s.T2, s.T3);

        var first = Slot(result, "17:00");
        first.GetProperty("startsAt").GetDateTime().Should().Be(Local(17));
        first.GetProperty("endsAt").GetDateTime().Should().Be(Local(19));
        first.GetProperty("options")[0].GetProperty("kind").GetString().Should().Be("RESOURCE");
    }

    [Fact]
    public async Task Turnover_buffer_and_preferred_time_shape_the_result()
    {
        var s = await CreateVenueWithTablesAsync();
        await Data.ExecuteAsync("UPDATE venues SET turnover_buffer_minutes = 30 WHERE id = @id", new { id = s.VenueId });
        await Data.SeedBookingAsync(s.TenantId, s.VenueId, s.T1, Local(19), Local(21));

        var result = await SearchAsync(s.Session, s.VenueId, 2, "&time=17:00&durationMinutes=90");

        result.GetProperty("turnoverMinutes").GetInt32().Should().Be(30);
        result.GetProperty("slots").EnumerateArray().Select(x => x.GetProperty("time").GetString())
            .Should().Equal("17:00", "17:15", "17:30", "17:45", "18:00", "18:15", "18:30");
        Offered(Slot(result, "17:00")).Should().Contain(s.T1, "17:00 + 90m + 30m ends exactly at 19:00");
        Offered(Slot(result, "17:15")).Should().NotContain(s.T1);
    }

    [Fact]
    public async Task Combinations_are_managed_and_offered_for_large_parties()
    {
        var s = await CreateVenueWithTablesAsync();

        var createdResponse = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/resource-combinations", new
        {
            venueId = s.VenueId,
            name = "T1 + T2",
            minCapacity = 5,
            capacity = 8,
            members = new object[] { new { resourceId = s.T1, isRequired = true }, new { resourceId = s.T2, isRequired = true } },
        });
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createdResponse.Content.ReadAsStringAsync(Ct));
        var combination = await createdResponse.JsonAsync();
        var combinationId = combination.GetProperty("id").GetGuid();
        combination.GetProperty("members").GetArrayLength().Should().Be(2);

        var duplicate = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/resource-combinations", new
        {
            venueId = s.VenueId, name = "t1 + t2", minCapacity = 5, capacity = 8,
            members = new object[] { new { resourceId = s.T2 }, new { resourceId = s.T3 } },
        });
        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "CONFLICT");

        var otherVenue = await PostIdAsync(s.Session, "/api/v1/venues",
            new { name = "Other", slug = "other-" + Guid.NewGuid().ToString("N")[..8], timeZone = "Africa/Johannesburg" });
        var foreign = await CreateTableAsync(s.Session, otherVenue, s.TypeId, "X1", 1);
        var crossVenue = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/resource-combinations", new
        {
            venueId = s.VenueId, name = "Cross", minCapacity = 5, capacity = 8,
            members = new object[] { new { resourceId = s.T1 }, new { resourceId = foreign } },
        });
        await crossVenue.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var single = await s.Session.SendAsync(HttpMethod.Post, "/api/v1/resource-combinations", new
        {
            venueId = s.VenueId, name = "Solo", minCapacity = 1, capacity = 4, members = new object[] { new { resourceId = s.T1 } },
        });
        await single.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var large = await SearchAsync(s.Session, s.VenueId, 7);
        var option = Slot(large, "17:00").GetProperty("options").EnumerateArray().Should().ContainSingle().Subject;
        option.GetProperty("kind").GetString().Should().Be("COMBINATION");
        option.GetProperty("id").GetGuid().Should().Be(combinationId);
        option.GetProperty("resourceIds").EnumerateArray().Select(e => e.GetGuid()).Should().BeEquivalentTo([s.T1, s.T2]);

        await Data.SeedBookingAsync(s.TenantId, s.VenueId, s.T2, Local(19), Local(21));
        var afterBooking = await SearchAsync(s.Session, s.VenueId, 7);
        Slot(afterBooking, "17:00").GetProperty("available").GetBoolean().Should().BeTrue("[17:00, 19:00) only touches the booking");
        Slot(afterBooking, "18:00").GetProperty("available").GetBoolean().Should().BeFalse();
        Slot(afterBooking, "21:00").GetProperty("available").GetBoolean().Should().BeTrue();

        var deactivate = await s.Session.SendAsync(HttpMethod.Put, $"/api/v1/resource-combinations/{combinationId}", new
        {
            name = "T1 + T2", minCapacity = 5, capacity = 8, isActive = false, isBookableOnline = true, sortOrder = 0,
            members = new object[] { new { resourceId = s.T1, isRequired = true }, new { resourceId = s.T2, isRequired = true } },
        });
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK, await deactivate.Content.ReadAsStringAsync(Ct));
        (await SearchAsync(s.Session, s.VenueId, 7)).GetProperty("slots").EnumerateArray()
            .Should().OnlyContain(x => !x.GetProperty("available").GetBoolean());

        var list = await (await s.Session.GetAsync($"/api/v1/resource-combinations?venueId={s.VenueId}")).JsonAsync();
        list.GetArrayLength().Should().Be(1);

        var deleted = await (await s.Session.SendAsync(HttpMethod.Delete, $"/api/v1/resource-combinations/{combinationId}")).JsonAsync();
        deleted.GetProperty("deleted").GetBoolean().Should().BeTrue();
        (await Data.ScalarAsync<long>(
                "SELECT count(*) FROM audit_logs WHERE entity_type = 'resource_combination' AND entity_id = @combinationId",
                new { combinationId }))
            .Should().Be(3);
    }

    [Fact]
    public async Task Section_and_type_filters_limit_the_candidate_resources()
    {
        var s = await CreateVenueWithTablesAsync();
        var sectionResponse = await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/venues/{s.VenueId}/sections", new { name = "Patio", sortOrder = 0 });
        var sectionId = (await sectionResponse.JsonAsync()).GetProperty("id").GetGuid();
        var patio = await CreateTableAsync(s.Session, s.VenueId, s.TypeId, "P1", 9, sectionId);
        var boothType = await PostIdAsync(s.Session, "/api/v1/resource-types", new { code = "BOOTH", name = "Booth" });
        var booth = await CreateTableAsync(s.Session, s.VenueId, boothType, "B1", 10);

        var inSection = await SearchAsync(s.Session, s.VenueId, 2, $"&sectionId={sectionId}");
        Offered(Slot(inSection, "17:00")).Should().Equal(patio);

        var ofType = await SearchAsync(s.Session, s.VenueId, 2, $"&resourceTypeId={boothType}");
        Offered(Slot(ofType, "17:00")).Should().Equal(booth);

        var foreignSection = await s.Session.GetAsync(
            $"/api/v1/availability?venueId={s.VenueId}&date={Date:yyyy-MM-dd}&partySize=2&sectionId={Guid.NewGuid()}");
        await foreignSection.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Closed_days_inactive_venues_validation_and_access_rules()
    {
        var s = await CreateVenueWithTablesAsync();

        var special = await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/venues/{s.VenueId}/special-hours",
            new { date = Date.ToString("yyyy-MM-dd"), name = "Private", isClosed = true });
        special.StatusCode.Should().Be(HttpStatusCode.Created, await special.Content.ReadAsStringAsync(Ct));
        var closed = await SearchAsync(s.Session, s.VenueId, 2);
        closed.GetProperty("isClosed").GetBoolean().Should().BeTrue();
        closed.GetProperty("slots").GetArrayLength().Should().Be(0);

        var badParty = await s.Session.GetAsync($"/api/v1/availability?venueId={s.VenueId}&date={Date:yyyy-MM-dd}&partySize=0");
        await badParty.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var badDuration = await s.Session.GetAsync($"/api/v1/availability?venueId={s.VenueId}&date={Date:yyyy-MM-dd}&partySize=2&durationMinutes=10");
        badDuration.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await badDuration.JsonAsync();
        problem.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        problem.GetProperty("errors").TryGetProperty("durationMinutes", out _).Should().BeTrue(problem.ToString());

        var missingVenue = await s.Session.GetAsync($"/api/v1/availability?date={Date:yyyy-MM-dd}&partySize=2");
        await missingVenue.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var (_, otherOwner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var otherSession = await SignInAsync(otherOwner);
        var crossTenant = await otherSession.GetAsync($"/api/v1/availability?venueId={s.VenueId}&date={Date:yyyy-MM-dd}&partySize=2");
        await crossTenant.ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");

        var host = await Data.CreateUserAsync();
        await Data.AddMembershipAsync(s.TenantId, host.UserId, "HOST");
        var hostSession = await SignInAsync(host);
        var unassigned = await hostSession.GetAsync($"/api/v1/availability?venueId={s.VenueId}&date={Date:yyyy-MM-dd}&partySize=2");
        await unassigned.ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");

        await Data.ExecuteAsync("UPDATE venues SET status = 'INACTIVE' WHERE id = @id", new { id = s.VenueId });
        await Data.ExecuteAsync("DELETE FROM special_operating_hours WHERE venue_id = @id", new { id = s.VenueId });
        (await SearchAsync(s.Session, s.VenueId, 2)).GetProperty("isClosed").GetBoolean().Should().BeTrue();
    }
}

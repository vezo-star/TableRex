using System.Net;
using System.Text.Json;
using BookingPlatform.IntegrationTests.Infrastructure;

namespace BookingPlatform.IntegrationTests.Venues;

public sealed class VenueAndResourceTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    private static object VenueBody(string slug, string name = "Main Venue", string timeZone = "Africa/Johannesburg") =>
        new { name, slug, timeZone, countryCode = "ZA" };

    private static async Task<Guid> CreateVenueAsync(ApiSession session, string? slug = null)
    {
        var response = await session.SendAsync(HttpMethod.Post, "/api/v1/venues", VenueBody(slug ?? "v-" + Guid.NewGuid().ToString("N")[..10]));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTypeAsync(ApiSession session, string code = "TABLE")
    {
        var response = await session.SendAsync(HttpMethod.Post, "/api/v1/resource-types", new { code, name = code });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateResourceAsync(ApiSession session, Guid venueId, Guid typeId, string code, Guid? sectionId = null)
    {
        var response = await session.SendAsync(HttpMethod.Post, "/api/v1/resources",
            new { venueId, sectionId, resourceTypeId = typeId, name = "Table " + code, code, minCapacity = 2, capacity = 4 });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateSectionAsync(ApiSession session, Guid venueId, string name)
    {
        var response = await session.SendAsync(HttpMethod.Post, $"/api/v1/venues/{venueId}/sections", new { name, sortOrder = 0 });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static IEnumerable<Guid> Ids(JsonElement array) => array.EnumerateArray().Select(e => e.GetProperty("id").GetGuid());

    [Fact]
    public async Task Owner_creates_and_updates_a_venue_with_default_rules_and_audit()
    {
        var (tenantId, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);

        var created = await session.SendAsync(HttpMethod.Post, "/api/v1/venues", VenueBody("main-venue"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var venue = await created.JsonAsync();
        venue.GetProperty("status").GetString().Should().Be("ACTIVE");
        venue.GetProperty("rules").GetProperty("slotIntervalMinutes").GetInt32().Should().Be(15);
        var venueId = venue.GetProperty("id").GetGuid();

        var duplicate = await session.SendAsync(HttpMethod.Post, "/api/v1/venues", VenueBody("main-venue", "Other"));
        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "SLUG_TAKEN");

        var rules = venue.GetProperty("rules").Deserialize<Dictionary<string, JsonElement>>()!;
        rules["maxPartySize"] = JsonSerializer.SerializeToElement(20);
        var updated = await session.SendAsync(HttpMethod.Put, $"/api/v1/venues/{venueId}",
            new { name = "Renamed", timeZone = "Europe/London", status = "INACTIVE", rules });
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        var after = await updated.JsonAsync();
        after.GetProperty("status").GetString().Should().Be("INACTIVE");
        after.GetProperty("timeZone").GetString().Should().Be("Europe/London");
        after.GetProperty("rules").GetProperty("maxPartySize").GetInt32().Should().Be(20);

        var badRules = await session.SendAsync(HttpMethod.Post, "/api/v1/venues",
            new { name = "Bad", slug = "bad-rules", timeZone = "Africa/Johannesburg", rules = new { slotIntervalMinutes = 7 } });
        await badRules.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        (await Data.ScalarAsync<long>(
                "SELECT count(*) FROM audit_logs WHERE tenant_id = @tenantId AND entity_type = 'venue' AND entity_id = @venueId",
                new { tenantId, venueId }))
            .Should().Be(2);
    }

    [Fact]
    public async Task Venues_and_resources_of_another_tenant_are_not_found()
    {
        var (_, ownerA, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var (_, ownerB, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var sessionA = await SignInAsync(ownerA);
        var venueA = await CreateVenueAsync(sessionA);
        var resourceA = await CreateResourceAsync(sessionA, venueA, await CreateTypeAsync(sessionA), "T1");
        var sessionB = await SignInAsync(ownerB);

        await (await sessionB.GetAsync($"/api/v1/venues/{venueA}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        await (await sessionB.GetAsync($"/api/v1/venues/{venueA}/sections")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        await (await sessionB.GetAsync($"/api/v1/floor-plans?venueId={venueA}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        await (await sessionB.GetAsync($"/api/v1/resources/{resourceA}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        await (await sessionB.SendAsync(HttpMethod.Delete, $"/api/v1/resources/{resourceA}"))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");

        var typeB = await CreateTypeAsync(sessionB);
        var crossCreate = await sessionB.SendAsync(HttpMethod.Post, "/api/v1/resources",
            new { venueId = venueA, resourceTypeId = typeB, name = "Sneaky", code = "X1", minCapacity = 1, capacity = 2 });
        await crossCreate.ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");

        Ids(await (await sessionB.GetAsync("/api/v1/venues")).JsonAsync()).Should().BeEmpty();
        (await (await sessionB.GetAsync("/api/v1/resources")).JsonAsync()).GetProperty("totalCount").GetInt64().Should().Be(0);
    }

    [Fact]
    public async Task Scoped_staff_only_see_assigned_venues()
    {
        var (tenantId, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var ownerSession = await SignInAsync(owner);
        var assigned = await CreateVenueAsync(ownerSession);
        var other = await CreateVenueAsync(ownerSession);
        var type = await CreateTypeAsync(ownerSession);
        await CreateResourceAsync(ownerSession, assigned, type, "A1");
        var otherResource = await CreateResourceAsync(ownerSession, other, type, "B1");

        var hostUser = await Data.CreateUserAsync();
        var host = await Data.AddMembershipAsync(tenantId, hostUser.UserId, "HOST");
        var assign = await ownerSession.SendAsync(HttpMethod.Put, $"/api/v1/staff/{host.TenantUserId}/venues", new { venueIds = new[] { assigned } });
        assign.StatusCode.Should().Be(HttpStatusCode.OK, await assign.Content.ReadAsStringAsync(Ct));
        (await assign.JsonAsync()).GetProperty("venueIds").EnumerateArray().Select(v => v.GetGuid()).Should().Equal(assigned);

        var hostSession = await SignInAsync(hostUser);
        Ids(await (await hostSession.GetAsync("/api/v1/venues")).JsonAsync()).Should().Equal(assigned);
        await (await hostSession.GetAsync($"/api/v1/venues/{other}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        await (await hostSession.GetAsync($"/api/v1/resources/{otherResource}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        await (await hostSession.GetAsync($"/api/v1/resources?venueId={other}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");

        var resources = await (await hostSession.GetAsync("/api/v1/resources")).JsonAsync();
        resources.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("venueId").GetGuid()).Should().OnlyContain(v => v == assigned);
        resources.GetProperty("totalCount").GetInt64().Should().Be(1);

        var hostMe = await (await hostSession.GetAsync("/api/v1/auth/me")).JsonAsync();
        hostMe.GetProperty("hasAllVenueAccess").GetBoolean().Should().BeFalse();
        hostMe.GetProperty("accessibleVenueIds").EnumerateArray().Select(v => v.GetGuid()).Should().Equal(assigned);

        var ownerMe = await (await ownerSession.GetAsync("/api/v1/auth/me")).JsonAsync();
        ownerMe.GetProperty("hasAllVenueAccess").GetBoolean().Should().BeTrue();
        ownerMe.GetProperty("accessibleVenueIds").EnumerateArray().Select(v => v.GetGuid()).Should().BeEquivalentTo([assigned, other]);

        var foreignVenue = await CreateVenueAsync(await SignInAsync((await Data.CreateTenantWithUserAsync("TENANT_OWNER")).User));
        var invalid = await ownerSession.SendAsync(HttpMethod.Put, $"/api/v1/staff/{host.TenantUserId}/venues", new { venueIds = new[] { foreignVenue } });
        await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        (await Data.ScalarAsync<long>(
                "SELECT count(*) FROM audit_logs WHERE tenant_id = @tenantId AND entity_type = 'tenant_user' AND action = 'venues.changed'",
                new { tenantId }))
            .Should().Be(1);
    }

    [Fact]
    public async Task Host_cannot_manage_venues_or_resources()
    {
        var (tenantId, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var ownerSession = await SignInAsync(owner);
        var venueId = await CreateVenueAsync(ownerSession);
        var type = await CreateTypeAsync(ownerSession);
        var hostUser = await Data.CreateUserAsync();
        var host = await Data.AddMembershipAsync(tenantId, hostUser.UserId, "HOST");
        await ownerSession.SendAsync(HttpMethod.Put, $"/api/v1/staff/{host.TenantUserId}/venues", new { venueIds = new[] { venueId } });
        var hostSession = await SignInAsync(hostUser);

        var resource = await hostSession.SendAsync(HttpMethod.Post, "/api/v1/resources",
            new { venueId, resourceTypeId = type, name = "T9", code = "T9", minCapacity = 1, capacity = 2 });
        await resource.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await hostSession.SendAsync(HttpMethod.Post, "/api/v1/venues", VenueBody("host-venue")))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await hostSession.SendAsync(HttpMethod.Post, "/api/v1/floor-plans", new { venueId, name = "Plan" }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");

        (await hostSession.GetAsync($"/api/v1/venues/{venueId}/sections")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await hostSession.GetAsync($"/api/v1/floor-plans?venueId={venueId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resource_rules_are_enforced()
    {
        var (_, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);
        var venueA = await CreateVenueAsync(session);
        var venueB = await CreateVenueAsync(session);
        var type = await CreateTypeAsync(session);
        var sectionB = await CreateSectionAsync(session, venueB, "Terrace");

        await (await session.SendAsync(HttpMethod.Post, "/api/v1/resource-types", new { code = "TABLE", name = "Again" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "CODE_TAKEN");
        await (await session.SendAsync(HttpMethod.Post, "/api/v1/resource-types", new { code = "table", name = "Lower" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var capacity = await session.SendAsync(HttpMethod.Post, "/api/v1/resources",
            new { venueId = venueA, resourceTypeId = type, name = "T1", code = "T1", minCapacity = 6, capacity = 4 });
        await capacity.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var foreignSection = await session.SendAsync(HttpMethod.Post, "/api/v1/resources",
            new { venueId = venueA, sectionId = sectionB, resourceTypeId = type, name = "T1", code = "T1", minCapacity = 1, capacity = 4 });
        await foreignSection.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var created = await session.SendAsync(HttpMethod.Post, "/api/v1/resources",
            new
            {
                venueId = venueA, resourceTypeId = type, name = "Window table", code = "T1", minCapacity = 2, capacity = 4,
                attributes = new Dictionary<string, string> { ["view"] = "window" },
            });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        (await created.JsonAsync()).GetProperty("attributes").GetProperty("view").GetString().Should().Be("window");

        await (await session.SendAsync(HttpMethod.Post, "/api/v1/resources",
                new { venueId = venueA, resourceTypeId = type, name = "Dup", code = "T1", minCapacity = 1, capacity = 2 }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "CODE_TAKEN");

        // The same code is allowed in a different venue.
        await CreateResourceAsync(session, venueB, type, "T1");
    }

    [Fact]
    public async Task Referenced_rows_are_deactivated_instead_of_deleted()
    {
        var (tenantId, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);
        var venueId = await CreateVenueAsync(session);
        var type = await CreateTypeAsync(session);
        var used = await CreateSectionAsync(session, venueId, "Main floor");
        var unused = await CreateSectionAsync(session, venueId, "Patio");
        var resource = await CreateResourceAsync(session, venueId, type, "T1", used);

        await (await session.SendAsync(HttpMethod.Post, $"/api/v1/venues/{venueId}/sections", new { name = "Patio", sortOrder = 1 }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "CONFLICT");

        var deactivated = await (await session.SendAsync(HttpMethod.Delete, $"/api/v1/venues/{venueId}/sections/{used}")).JsonAsync();
        deactivated.GetProperty("deleted").GetBoolean().Should().BeFalse();
        deactivated.GetProperty("deactivated").GetBoolean().Should().BeTrue();

        var deleted = await (await session.SendAsync(HttpMethod.Delete, $"/api/v1/venues/{venueId}/sections/{unused}")).JsonAsync();
        deleted.GetProperty("deleted").GetBoolean().Should().BeTrue();

        var sections = await (await session.GetAsync($"/api/v1/venues/{venueId}/sections")).JsonAsync();
        sections.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("isActive").GetBoolean().Should().BeFalse();

        // A resource with no references is deleted outright.
        var removed = await (await session.SendAsync(HttpMethod.Delete, $"/api/v1/resources/{resource}")).JsonAsync();
        removed.GetProperty("deleted").GetBoolean().Should().BeTrue();
        await (await session.GetAsync($"/api/v1/resources/{resource}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");

        (await Data.ScalarAsync<long>(
                "SELECT count(*) FROM audit_logs WHERE tenant_id = @tenantId AND entity_type = 'venue_section' AND action IN ('deleted', 'deactivated')",
                new { tenantId }))
            .Should().Be(2);
    }

    [Fact]
    public async Task Floor_plan_positions_must_fit_and_belong_to_the_venue()
    {
        var (tenantId, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);
        var venueId = await CreateVenueAsync(session);
        var otherVenue = await CreateVenueAsync(session);
        var type = await CreateTypeAsync(session);
        var t1 = await CreateResourceAsync(session, venueId, type, "T1");
        var t2 = await CreateResourceAsync(session, venueId, type, "T2");
        var foreign = await CreateResourceAsync(session, otherVenue, type, "X1");

        var created = await session.SendAsync(HttpMethod.Post, "/api/v1/floor-plans", new { venueId, name = "Ground", width = 1000, height = 800 });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var planId = (await created.JsonAsync()).GetProperty("id").GetGuid();

        object Position(Guid id, decimal x, decimal y, string shape = "RECT") => new { resourceId = id, x, y, width = 80, height = 80, rotation = 45, shape };

        await (await session.SendAsync(HttpMethod.Put, $"/api/v1/floor-plans/{planId}/resources", new { positions = new[] { Position(foreign, 10, 10) } }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await session.SendAsync(HttpMethod.Put, $"/api/v1/floor-plans/{planId}/resources", new { positions = new[] { Position(t1, 950, 10) } }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var saved = await session.SendAsync(HttpMethod.Put, $"/api/v1/floor-plans/{planId}/resources",
            new { positions = new[] { Position(t1, 10, 10), Position(t2, 200, 300, "ROUND") } });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
        var plan = await saved.JsonAsync();
        plan.GetProperty("positions").GetArrayLength().Should().Be(2);
        plan.GetProperty("positions").EnumerateArray().Single(p => p.GetProperty("resourceId").GetGuid() == t2)
            .GetProperty("shape").GetString().Should().Be("ROUND");

        // Replacing with a subset removes the rest.
        var replaced = await (await session.SendAsync(HttpMethod.Put, $"/api/v1/floor-plans/{planId}/resources",
            new { positions = new[] { Position(t2, 400, 300) } })).JsonAsync();
        replaced.GetProperty("positions").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("x").GetDecimal().Should().Be(400);

        var list = await (await session.GetAsync($"/api/v1/floor-plans?venueId={venueId}")).JsonAsync();
        Ids(list).Should().Equal(planId);

        (await Data.ScalarAsync<long>(
                "SELECT count(*) FROM audit_logs WHERE tenant_id = @tenantId AND entity_type = 'floor_plan' AND action = 'moved'",
                new { tenantId }))
            .Should().Be(2);
    }

    [Fact]
    public async Task Operating_hours_support_overnight_periods_and_reject_overlaps()
    {
        var (_, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);
        var venueId = await CreateVenueAsync(session);

        var overlap = await session.SendAsync(HttpMethod.Put, $"/api/v1/venues/{venueId}/operating-hours", new
        {
            periods = new object[]
            {
                new { dayOfWeek = 5, openTime = "18:00", closeTime = "02:00" },
                new { dayOfWeek = 6, openTime = "01:00", closeTime = "10:00" },
            },
        });
        await overlap.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var saved = await session.SendAsync(HttpMethod.Put, $"/api/v1/venues/{venueId}/operating-hours", new
        {
            periods = new object[]
            {
                new { dayOfWeek = 5, openTime = "18:00", closeTime = "02:00" },
                new { dayOfWeek = 5, openTime = "11:00", closeTime = "15:00" },
                new { dayOfWeek = 6, openTime = "12:00", closeTime = "23:00" },
            },
        });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
        var hours = await (await session.GetAsync($"/api/v1/venues/{venueId}/operating-hours")).JsonAsync();
        hours.GetArrayLength().Should().Be(3);
        hours[0].GetProperty("openTime").GetString().Should().Be("11:00");

        // 2026-10-09 is a Friday; venue is Africa/Johannesburg (UTC+2).
        var schedule = await (await session.GetAsync($"/api/v1/venues/{venueId}/schedule?from=2026-10-09&to=2026-10-11")).JsonAsync();
        var days = schedule.GetProperty("days").EnumerateArray().ToList();
        days.Should().HaveCount(3);
        var friday = days[0].GetProperty("periods").EnumerateArray().ToList();
        friday.Should().HaveCount(2);
        friday[1].GetProperty("overnight").GetBoolean().Should().BeTrue();
        friday[1].GetProperty("endsAt").GetDateTime().ToUniversalTime().Should().Be(new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        days[2].GetProperty("isClosed").GetBoolean().Should().BeTrue();

        await (await session.GetAsync($"/api/v1/venues/{venueId}/schedule?from=2026-10-01&to=2026-12-31"))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Special_hours_override_the_week_and_reject_conflicts()
    {
        var (_, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);
        var venueId = await CreateVenueAsync(session);
        await session.SendAsync(HttpMethod.Put, $"/api/v1/venues/{venueId}/operating-hours",
            new { periods = new[] { new { dayOfWeek = 5, openTime = "18:00", closeTime = "23:00" } } });

        var closed = await session.SendAsync(HttpMethod.Post, $"/api/v1/venues/{venueId}/special-hours",
            new { date = "2026-10-09", name = "Private event", isClosed = true });
        closed.StatusCode.Should().Be(HttpStatusCode.Created, await closed.Content.ReadAsStringAsync(Ct));

        await (await session.SendAsync(HttpMethod.Post, $"/api/v1/venues/{venueId}/special-hours",
                new { date = "2026-10-09", isClosed = false, openTime = "10:00", closeTime = "12:00" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "CONFLICT");

        await (await session.SendAsync(HttpMethod.Post, $"/api/v1/venues/{venueId}/special-hours",
                new { date = "2026-10-10", isClosed = true, openTime = "10:00" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var first = await session.SendAsync(HttpMethod.Post, $"/api/v1/venues/{venueId}/special-hours",
            new { date = "2026-10-16", isClosed = false, openTime = "10:00", closeTime = "14:00" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        (await first.JsonAsync()).GetProperty("openTime").GetString().Should().Be("10:00");
        await (await session.SendAsync(HttpMethod.Post, $"/api/v1/venues/{venueId}/special-hours",
                new { date = "2026-10-16", isClosed = false, openTime = "13:00", closeTime = "16:00" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var schedule = await (await session.GetAsync($"/api/v1/venues/{venueId}/schedule?from=2026-10-09&to=2026-10-16")).JsonAsync();
        var days = schedule.GetProperty("days").EnumerateArray().ToList();
        days[0].GetProperty("isSpecial").GetBoolean().Should().BeTrue();
        days[0].GetProperty("isClosed").GetBoolean().Should().BeTrue();
        days[7].GetProperty("periods").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("openTime").GetString().Should().Be("10:00");

        var closedId = (await closed.JsonAsync()).GetProperty("id").GetGuid();
        (await session.SendAsync(HttpMethod.Delete, $"/api/v1/venues/{venueId}/special-hours/{closedId}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        var restored = await (await session.GetAsync($"/api/v1/venues/{venueId}/schedule?from=2026-10-09&to=2026-10-09")).JsonAsync();
        restored.GetProperty("days")[0].GetProperty("periods").GetArrayLength().Should().Be(1);
    }
}

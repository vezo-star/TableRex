using System.Net;
using System.Text;
using System.Text.Json;
using BookingPlatform.IntegrationTests.Bookings;
using BookingPlatform.IntegrationTests.Infrastructure;

namespace BookingPlatform.IntegrationTests.Saas;

public sealed class SaasTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    private const string StaffPassword = "Quiet-River-Lantern-42";

    private async Task<(Guid TenantId, ApiSession Owner)> TenantOnPlanAsync(string plan)
    {
        var tenantId = await Data.CreateTenantAsync(plan: plan);
        var owner = await Data.CreateUserAsync();
        await Data.AddMembershipAsync(tenantId, owner.UserId, "TENANT_OWNER");
        return (tenantId, await SignInAsync(owner));
    }

    private async Task<ApiSession> PlatformAdminAsync() => await SignInAsync(await Data.CreateUserAsync(platformAdmin: true));

    private static Task<HttpResponseMessage> CreateVenueAsync(ApiSession session, string? slug = null)
    {
        slug ??= "v-" + Guid.NewGuid().ToString("N")[..10];
        return session.SendAsync(HttpMethod.Post, "/api/v1/venues", new { name = "Venue " + slug, slug, timeZone = "Africa/Johannesburg" });
    }

    private static async Task<Guid> CreateVenueOkAsync(ApiSession session, string? slug = null)
    {
        var response = await CreateVenueAsync(session, slug);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> CreateStaffAsync(ApiSession session) =>
        session.SendAsync(HttpMethod.Post, "/api/v1/staff",
            new { email = $"s-{Guid.NewGuid():N}@example.test", displayName = "Staff Member", password = StaffPassword, roles = new[] { "STAFF" } });

    private static Task<HttpResponseMessage> CreateResourceAsync(ApiSession session, Guid venueId, Guid typeId, string code) =>
        session.SendAsync(HttpMethod.Post, "/api/v1/resources",
            new { venueId, resourceTypeId = typeId, name = code, code, minCapacity = 1, capacity = 4 });

    private static Task<HttpResponseMessage> SetPlanAsync(ApiSession admin, Guid tenantId, object body) =>
        admin.SendAsync(HttpMethod.Put, $"/api/v1/platform/tenants/{tenantId}/subscription", body);

    [Fact]
    public async Task Venue_limit_blocks_additions_until_a_platform_admin_raises_it()
    {
        var (tenantId, owner) = await TenantOnPlanAsync("STARTER");
        await CreateVenueOkAsync(owner);
        await (await CreateVenueAsync(owner)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PLAN_LIMIT_REACHED");

        await (await SetPlanAsync(owner, tenantId, new { planCode = "PROFESSIONAL" })).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        var admin = await PlatformAdminAsync();
        await (await SetPlanAsync(admin, tenantId, new { planCode = "GOLD" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await SetPlanAsync(admin, tenantId, new { planCode = "STARTER", maxVenuesOverride = 0 }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var raised = await SetPlanAsync(admin, tenantId, new { planCode = "STARTER", maxVenuesOverride = 2 });
        raised.StatusCode.Should().Be(HttpStatusCode.OK, await raised.Content.ReadAsStringAsync(Ct));

        await CreateVenueOkAsync(owner);
        var subscription = await (await owner.GetAsync("/api/v1/tenants/current/subscription")).JsonAsync();
        subscription.GetProperty("plan").GetProperty("code").GetString().Should().Be("STARTER");
        subscription.GetProperty("plan").GetProperty("maxVenues").GetInt32().Should().Be(1);
        subscription.GetProperty("limits").GetProperty("maxVenues").GetInt32().Should().Be(2);
        subscription.GetProperty("overrides").GetProperty("maxVenues").GetInt32().Should().Be(2);
        subscription.GetProperty("usage").GetProperty("venues").GetInt32().Should().Be(2);
        subscription.GetProperty("usage").GetProperty("activeStaff").GetInt32().Should().Be(1);
        await (await CreateVenueAsync(owner)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PLAN_LIMIT_REACHED");

        // Downgrading never removes venues; it only blocks new ones.
        (await SetPlanAsync(admin, tenantId, new { planCode = "STARTER" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM venues WHERE tenant_id = @tenantId", new { tenantId })).Should().Be(2);
        (await Data.ScalarAsync<long>(
                "SELECT count(*) FROM audit_logs WHERE tenant_id = @tenantId AND entity_type = 'tenant_subscription'", new { tenantId }))
            .Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_venue_creation_cannot_exceed_the_limit()
    {
        var (tenantId, owner) = await TenantOnPlanAsync("STARTER");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CreateVenueAsync(owner)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).Should().Be(5);
        (await Data.ScalarAsync<long>("SELECT count(*) FROM venues WHERE tenant_id = @tenantId", new { tenantId })).Should().Be(1);
    }

    [Fact]
    public async Task Staff_limit_counts_active_members_and_applies_to_reactivation()
    {
        var (tenantId, owner) = await TenantOnPlanAsync("STARTER");
        var admin = await PlatformAdminAsync();
        (await SetPlanAsync(admin, tenantId, new { planCode = "STARTER", maxActiveStaffOverride = 2 })).StatusCode.Should().Be(HttpStatusCode.OK);

        var first = await CreateStaffAsync(owner);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(Ct));
        var firstId = (await first.JsonAsync()).GetProperty("staff").GetProperty("tenantUserId").GetGuid();
        await (await CreateStaffAsync(owner)).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PLAN_LIMIT_REACHED");

        (await owner.SendAsync(HttpMethod.Post, $"/api/v1/staff/{firstId}/deactivate")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreateStaffAsync(owner)).StatusCode.Should().Be(HttpStatusCode.Created);
        await (await owner.SendAsync(HttpMethod.Post, $"/api/v1/staff/{firstId}/activate"))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PLAN_LIMIT_REACHED");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM tenant_users WHERE tenant_id = @tenantId AND status = 'ACTIVE'", new { tenantId }))
            .Should().Be(2);
    }

    [Fact]
    public async Task Resource_limit_applies_per_venue_and_to_reactivation()
    {
        var (tenantId, owner) = await TenantOnPlanAsync("ENTERPRISE");
        var admin = await PlatformAdminAsync();
        (await SetPlanAsync(admin, tenantId, new { planCode = "ENTERPRISE", maxResourcesPerVenueOverride = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);
        var venueA = await CreateVenueOkAsync(owner);
        var venueB = await CreateVenueOkAsync(owner);
        var typeId = await BookingScenario.PostIdAsync(owner, "/api/v1/resource-types", new { code = "TABLE", name = "Table" });

        var first = await CreateResourceAsync(owner, venueA, typeId, "A1");
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstId = (await first.JsonAsync()).GetProperty("id").GetGuid();
        await (await CreateResourceAsync(owner, venueA, typeId, "A2")).ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PLAN_LIMIT_REACHED");
        (await CreateResourceAsync(owner, venueB, typeId, "B1")).StatusCode.Should().Be(HttpStatusCode.Created);

        object Update(bool isActive) => new { resourceTypeId = typeId, name = "A1", code = "A1", minCapacity = 1, capacity = 4, isActive, isBookableOnline = true, sortOrder = 0 };
        (await owner.SendAsync(HttpMethod.Put, $"/api/v1/resources/{firstId}", Update(false))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CreateResourceAsync(owner, venueA, typeId, "A2")).StatusCode.Should().Be(HttpStatusCode.Created);
        await (await owner.SendAsync(HttpMethod.Put, $"/api/v1/resources/{firstId}", Update(true)))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PLAN_LIMIT_REACHED");
    }

    [Fact]
    public async Task Branding_is_a_plan_feature_validated_and_shown_on_public_pages()
    {
        var tenantSlug = "brand-" + Guid.NewGuid().ToString("N")[..8];
        var tenantId = await Data.CreateTenantAsync(tenantSlug, "Brand Co", plan: "STARTER");
        var ownerUser = await Data.CreateUserAsync();
        await Data.AddMembershipAsync(tenantId, ownerUser.UserId, "TENANT_OWNER");
        var owner = await SignInAsync(ownerUser);
        await CreateVenueOkAsync(owner, "harbour");
        var brand = new { primaryColor = "#1A73E8", logoUrl = "https://cdn.example.test/logo.png", bookingPageIntro = "  Welcome!  ", bookingTerms = "No refunds." };

        await (await owner.SendAsync(HttpMethod.Put, "/api/v1/tenants/current/branding", brand))
            .ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, "PLAN_FEATURE_UNAVAILABLE");
        (await (await owner.GetAsync("/api/v1/tenants/current/branding")).JsonAsync()).GetProperty("enabled").GetBoolean().Should().BeFalse();

        var admin = await PlatformAdminAsync();
        (await SetPlanAsync(admin, tenantId, new { planCode = "PROFESSIONAL" })).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var bad in new object[]
                 {
                     new { primaryColor = "red" },
                     new { logoUrl = "javascript:alert(1)" },
                     new { logoUrl = "http://cdn.example.test/logo.png" },
                     new { logoUrl = "https://cdn.example.test/a\"onerror=\"x.png" },
                     new { bookingTerms = new string('x', 2001) },
                 })
        {
            await (await owner.SendAsync(HttpMethod.Put, "/api/v1/tenants/current/branding", bad)).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        }

        var saved = await owner.SendAsync(HttpMethod.Put, "/api/v1/tenants/current/branding", brand);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Ct));
        var savedBody = await saved.JsonAsync();
        savedBody.GetProperty("primaryColor").GetString().Should().Be("#1a73e8");
        savedBody.GetProperty("bookingPageIntro").GetString().Should().Be("Welcome!");
        savedBody.GetProperty("enabled").GetBoolean().Should().BeTrue();

        var guest = NewSession();
        var venue = await (await guest.GetAsync($"/api/v1/public/{tenantSlug}/harbour")).JsonAsync();
        venue.GetProperty("branding").GetProperty("logoUrl").GetString().Should().Be("https://cdn.example.test/logo.png");
        var tenantPage = await guest.GetAsync($"/api/v1/public/{tenantSlug}");
        tenantPage.StatusCode.Should().Be(HttpStatusCode.OK, await tenantPage.Content.ReadAsStringAsync(Ct));
        var tenant = await tenantPage.JsonAsync();
        tenant.GetProperty("name").GetString().Should().Be("Brand Co");
        tenant.GetProperty("branding").GetProperty("primaryColor").GetString().Should().Be("#1a73e8");
        tenant.GetProperty("venues").EnumerateArray().Select(v => v.GetProperty("slug").GetString()).Should().Equal("harbour");
        tenant.GetRawText().Should().NotContain(tenantId.ToString());

        // A downgrade keeps the stored values but stops showing them.
        (await SetPlanAsync(admin, tenantId, new { planCode = "STARTER" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await guest.GetAsync($"/api/v1/public/{tenantSlug}/harbour")).JsonAsync()).GetProperty("branding").ValueKind.Should().Be(JsonValueKind.Null);
        (await (await guest.GetAsync($"/api/v1/public/{tenantSlug}")).JsonAsync()).GetProperty("branding").ValueKind.Should().Be(JsonValueKind.Null);
        (await (await owner.GetAsync("/api/v1/tenants/current/branding")).JsonAsync()).GetProperty("logoUrl").GetString().Should().NotBeNull();
    }

    [Fact]
    public async Task Public_tenant_page_lists_only_bookable_venues_of_active_tenants()
    {
        var tenantSlug = "multi-" + Guid.NewGuid().ToString("N")[..8];
        var tenantId = await Data.CreateTenantAsync(tenantSlug, "Multi Co", plan: "PROFESSIONAL");
        var ownerUser = await Data.CreateUserAsync();
        await Data.AddMembershipAsync(tenantId, ownerUser.UserId, "TENANT_OWNER");
        var owner = await SignInAsync(ownerUser);
        await CreateVenueOkAsync(owner, "bravo");
        await CreateVenueOkAsync(owner, "alpha");
        var hidden = await CreateVenueOkAsync(owner, "closed");
        var offline = await CreateVenueOkAsync(owner, "offline");
        await Data.ExecuteAsync("UPDATE venues SET status = 'INACTIVE' WHERE id = @hidden", new { hidden });
        await Data.ExecuteAsync("UPDATE venues SET public_booking_enabled = false WHERE id = @offline", new { offline });

        var guest = NewSession();
        var page = await (await guest.GetAsync($"/api/v1/public/{tenantSlug}")).JsonAsync();
        page.GetProperty("venues").EnumerateArray().Select(v => v.GetProperty("slug").GetString()).Should().Equal("alpha", "bravo");
        page.GetProperty("branding").ValueKind.Should().Be(JsonValueKind.Null);

        await (await guest.GetAsync("/api/v1/public/no-such-business")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "TENANT_NOT_FOUND");
        await Data.ExecuteAsync("UPDATE tenants SET status = 'SUSPENDED' WHERE id = @tenantId", new { tenantId });
        await (await guest.GetAsync($"/api/v1/public/{tenantSlug}")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "TENANT_NOT_FOUND");
    }

    [Fact]
    public async Task Platform_admin_assigns_a_plan_when_creating_a_tenant()
    {
        var admin = await PlatformAdminAsync();
        object Request(string slug, string? planCode) => new
        {
            name = "Planned Co",
            slug,
            defaultTimeZone = "Africa/Johannesburg",
            owner = new { email = $"o-{Guid.NewGuid():N}@example.test", displayName = "Owner", password = StaffPassword },
            planCode,
        };

        var professional = await admin.SendAsync(HttpMethod.Post, "/api/v1/platform/tenants", Request("pro-" + Guid.NewGuid().ToString("N")[..8], "PROFESSIONAL"));
        professional.StatusCode.Should().Be(HttpStatusCode.Created, await professional.Content.ReadAsStringAsync(Ct));
        (await professional.JsonAsync()).GetProperty("planCode").GetString().Should().Be("PROFESSIONAL");

        var defaulted = await admin.SendAsync(HttpMethod.Post, "/api/v1/platform/tenants", Request("std-" + Guid.NewGuid().ToString("N")[..8], null));
        var defaultedBody = await defaulted.JsonAsync();
        defaultedBody.GetProperty("planCode").GetString().Should().Be("STARTER");
        var subscription = await (await admin.GetAsync($"/api/v1/platform/tenants/{defaultedBody.GetProperty("id").GetGuid()}/subscription")).JsonAsync();
        subscription.GetProperty("limits").GetProperty("maxVenues").GetInt32().Should().Be(1);
        subscription.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.Null);

        await (await admin.SendAsync(HttpMethod.Post, "/api/v1/platform/tenants", Request("gold-" + Guid.NewGuid().ToString("N")[..8], "GOLD")))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await admin.SendAsync(HttpMethod.Post, "/api/v1/platform/tenants", Request("booking", null)))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await admin.GetAsync($"/api/v1/platform/tenants/{Guid.NewGuid()}/subscription")).ShouldBeProblemAsync(HttpStatusCode.NotFound, "TENANT_NOT_FOUND");

        var plans = await (await admin.GetAsync("/api/v1/plans")).JsonAsync();
        plans.EnumerateArray().Select(p => p.GetProperty("code").GetString()).Should().Equal("STARTER", "PROFESSIONAL", "ENTERPRISE");
        (await NewSession().GetAsync("/api/v1/plans")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bookings_summary_reports_counts_rates_series_and_deposits()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        var noShow = (await s.CreateBookingOkAsync(s.Booking("19:00", partySize: 2))).GetProperty("id").GetGuid();
        await s.CreateBookingOkAsync(s.Booking("20:00", partySize: 4, source: "PHONE"));
        var cancelled = (await s.CreateBookingOkAsync(s.Booking("21:00", partySize: 3))).GetProperty("id").GetGuid();
        (await s.Session.SendAsync(HttpMethod.Post, $"/api/v1/bookings/{cancelled}/cancel", new { reason = "Changed plans" })).StatusCode.Should().Be(HttpStatusCode.OK);
        await Data.ExecuteAsync(
            """
            UPDATE booking_allocations SET is_active = false, released_at = now(), release_reason = 'NO_SHOW' WHERE booking_id = @noShow AND is_active;
            UPDATE bookings SET status = 'NO_SHOW', no_show_at = now() WHERE id = @noShow;
            """,
            new { noShow });

        var from = BookingScenario.Date.AddDays(-1).ToString("yyyy-MM-dd");
        var response = await s.Session.GetAsync($"/api/v1/reports/bookings-summary?venueId={s.VenueId}&from={from}&to={BookingScenario.DateText}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var report = await response.JsonAsync();

        report.GetProperty("totalBookings").GetInt32().Should().Be(3);
        report.GetProperty("bookings").GetInt32().Should().Be(2);
        report.GetProperty("covers").GetInt32().Should().Be(6);
        report.GetProperty("cancelled").GetInt32().Should().Be(1);
        report.GetProperty("noShows").GetInt32().Should().Be(1);
        report.GetProperty("cancellationRate").GetDecimal().Should().Be(0.3333m);
        report.GetProperty("noShowRate").GetDecimal().Should().Be(0.5m);
        report.GetProperty("averagePartySize").GetDecimal().Should().Be(3m);
        report.GetProperty("bySource").EnumerateArray().Select(x => (x.GetProperty("key").GetString(), x.GetProperty("bookings").GetInt32()))
            .Should().BeEquivalentTo(new[] { ("PHONE", 1), ("STAFF", 1) });
        report.GetProperty("byHour").EnumerateArray().Select(x => (x.GetProperty("hour").GetInt32(), x.GetProperty("covers").GetInt32()))
            .Should().Equal((19, 2), (20, 4));
        var daily = report.GetProperty("daily").EnumerateArray().ToList();
        daily.Should().HaveCount(2);
        daily[0].GetProperty("bookings").GetInt32().Should().Be(0);
        daily[1].GetProperty("date").GetString().Should().Be(BookingScenario.DateText);
        daily[1].GetProperty("bookings").GetInt32().Should().Be(2);
        daily[1].GetProperty("cancelled").GetInt32().Should().Be(1);
        daily[1].GetProperty("noShows").GetInt32().Should().Be(1);
        report.GetProperty("deposits").GetArrayLength().Should().Be(0);

        await (await s.Session.GetAsync($"/api/v1/reports/bookings-summary?venueId={s.VenueId}&from={BookingScenario.DateText}&to={from}"))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var tooLong = BookingScenario.Date.AddDays(-366).ToString("yyyy-MM-dd");
        await (await s.Session.GetAsync($"/api/v1/reports/bookings-summary?venueId={s.VenueId}&from={tooLong}&to={BookingScenario.DateText}"))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Reports_respect_permissions_venue_scope_and_tenant_isolation()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        var query = $"/api/v1/reports/bookings-summary?venueId={s.VenueId}&from={BookingScenario.DateText}&to={BookingScenario.DateText}";

        var manager = await Data.CreateUserAsync();
        await Data.AddMembershipAsync(s.TenantId, manager.UserId, "MANAGER");
        await (await (await SignInAsync(manager)).GetAsync(query)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");

        var host = await Data.CreateUserAsync();
        await Data.AddMembershipAsync(s.TenantId, host.UserId, "HOST");
        await (await (await SignInAsync(host)).GetAsync(query)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");

        var (_, otherOwner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var other = await SignInAsync(otherOwner);
        await (await other.GetAsync(query)).ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        await (await other.GetAsync(query.Replace("bookings-summary", "bookings.csv", StringComparison.Ordinal)))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "VENUE_NOT_FOUND");
        (await other.GetAsync("/api/v1/tenants/current/subscription")).StatusCode.Should().Be(HttpStatusCode.OK);
        await (await (await SignInAsync(host)).GetAsync("/api/v1/tenants/current/subscription")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Csv_export_neutralises_spreadsheet_formulas()
    {
        var s = await BookingScenario.CreateAsync(Data, SignInAsync);
        await s.CreateBookingOkAsync(s.Booking("19:00",
            customer: new { firstName = "=HYPERLINK(\"https://evil.example\")", lastName = "Smith", email = $"x-{Guid.NewGuid():N}@example.test" }));
        await s.CreateBookingOkAsync(s.Booking("20:00", customer: new { firstName = "Zanele", lastName = "Dube, Jr", email = $"z-{Guid.NewGuid():N}@example.test" }));

        var response = await s.Session.GetAsync($"/api/v1/reports/bookings.csv?venueId={s.VenueId}&from={BookingScenario.DateText}&to={BookingScenario.DateText}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("bookings-bistro-");
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines.Should().HaveCount(3);
        lines[0].Should().StartWith("Reference,Date,Time,");
        lines[1].Should().Contain("\"19:00\"").And.Contain("\"'=HYPERLINK(\"\"https://evil.example\"\") Smith\"");
        lines[2].Should().Contain("\"Zanele Dube, Jr\"");
        lines.Should().NotContain(l => l.Contains("@example.test", StringComparison.Ordinal), "the export carries no contact details");
    }
}

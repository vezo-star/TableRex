using System.Net;
using BookingPlatform.Application.Identity;
using BookingPlatform.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BookingPlatform.IntegrationTests.Auth;

public sealed class TenancyAndPermissionTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Staff_of_another_tenant_is_not_found_rather_than_forbidden()
    {
        var (_, ownerA, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var (_, _, memberB) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(ownerA);

        var response = await session.GetAsync($"/api/v1/staff/{memberB.TenantUserId}");
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "STAFF_NOT_FOUND");

        var roles = await session.SendAsync(HttpMethod.Put, $"/api/v1/staff/{memberB.TenantUserId}/roles", new { roles = new[] { "VIEW_ONLY" } });
        await roles.ShouldBeProblemAsync(HttpStatusCode.NotFound, "STAFF_NOT_FOUND");

        var list = await (await session.GetAsync("/api/v1/staff")).JsonAsync();
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("tenantUserId").GetGuid())
            .Should().NotContain(memberB.TenantUserId);
        list.GetProperty("totalCount").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task Token_carrying_a_tenant_without_membership_is_rejected()
    {
        var (_, userA, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var tenantB = await Data.CreateTenantAsync();
        var forged = Factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userA.UserId, tenantB);
        var session = NewSession();
        session.UseAccessToken(forged.Token);

        await (await session.GetAsync("/api/v1/tenants/current")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await session.GetAsync("/api/v1/staff")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Theory]
    [InlineData("VIEW_ONLY", "/api/v1/staff", HttpStatusCode.Forbidden)]
    [InlineData("STAFF", "/api/v1/roles", HttpStatusCode.Forbidden)]
    [InlineData("HOST", "/api/v1/staff", HttpStatusCode.Forbidden)]
    [InlineData("MANAGER", "/api/v1/staff", HttpStatusCode.OK)]
    [InlineData("VIEW_ONLY", "/api/v1/tenants/current", HttpStatusCode.OK)]
    public async Task Read_endpoints_enforce_the_permission_matrix(string role, string path, HttpStatusCode expected)
    {
        var (_, user, _) = await Data.CreateTenantWithUserAsync(role);
        var session = await SignInAsync(user);

        var response = await session.GetAsync(path);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Tenant_settings_require_settings_manage()
    {
        var (tenantId, manager, _) = await Data.CreateTenantWithUserAsync("MANAGER");
        var admin = await Data.CreateUserAsync();
        await Data.AddMembershipAsync(tenantId, admin.UserId, "TENANT_ADMIN");

        var managerSession = await SignInAsync(manager);
        var denied = await managerSession.SendAsync(HttpMethod.Put, "/api/v1/tenants/current", new { name = "Renamed", defaultTimeZone = "Europe/London" });
        await denied.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");

        var adminSession = await SignInAsync(admin);
        var invalid = await adminSession.SendAsync(HttpMethod.Put, "/api/v1/tenants/current", new { name = "Renamed", defaultTimeZone = "Mars/Olympus" });
        await invalid.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var ok = await adminSession.SendAsync(HttpMethod.Put, "/api/v1/tenants/current", new { name = "Renamed", defaultTimeZone = "Europe/London" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.JsonAsync()).GetProperty("defaultTimeZone").GetString().Should().Be("Europe/London");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE tenant_id = @tenantId AND entity_type = 'tenant' AND action = 'updated'", new { tenantId }))
            .Should().Be(1);
    }

    [Fact]
    public async Task Owner_can_create_staff_who_can_then_sign_in()
    {
        var (tenantId, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);
        var email = $"new-{Guid.NewGuid():N}@example.test";

        var created = await session.SendAsync(HttpMethod.Post, "/api/v1/staff",
            new { email, displayName = "New Host", password = "Hosting-is-fun-2026", roles = new[] { "HOST" } });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.JsonAsync();
        body.GetProperty("accountCreated").GetBoolean().Should().BeTrue();
        body.GetProperty("staff").GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("HOST");

        var duplicate = await session.SendAsync(HttpMethod.Post, "/api/v1/staff",
            new { email, displayName = "Again", password = "Hosting-is-fun-2026", roles = new[] { "HOST" } });
        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "EMAIL_TAKEN");

        var host = await NewSession().LoginOkAsync(email, "Hosting-is-fun-2026");
        host.LastAuthBody.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        (await Data.ScalarAsync<string>("SELECT password_hash FROM users WHERE email = @email", new { email }))
            .Should().NotContain("Hosting-is-fun-2026");
    }

    [Fact]
    public async Task Staff_creation_validates_input()
    {
        var (_, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);

        var response = await session.SendAsync(HttpMethod.Post, "/api/v1/staff",
            new { email = "not-an-email", displayName = "", password = "short", roles = new[] { "SUPERUSER" } });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var errors = (await response.JsonAsync()).GetProperty("errors");
        errors.EnumerateObject().Select(e => e.Name).Should().Contain(["email", "displayName", "password"]);
    }

    [Fact]
    public async Task Last_owner_cannot_be_demoted_or_deactivated()
    {
        var (_, owner, ownerMembership) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);

        var demote = await session.SendAsync(HttpMethod.Put, $"/api/v1/staff/{ownerMembership.TenantUserId}/roles", new { roles = new[] { "TENANT_ADMIN" } });
        await demote.ShouldBeProblemAsync((HttpStatusCode)422, "LAST_OWNER");

        var deactivateSelf = await session.SendAsync(HttpMethod.Post, $"/api/v1/staff/{ownerMembership.TenantUserId}/deactivate");
        await deactivateSelf.ShouldBeProblemAsync((HttpStatusCode)422, "BUSINESS_RULE_VIOLATION");
    }

    [Fact]
    public async Task Admin_cannot_grant_or_remove_the_owner_role()
    {
        var (tenantId, _, ownerMembership) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var admin = await Data.CreateUserAsync();
        var adminMembership = await Data.AddMembershipAsync(tenantId, admin.UserId, "TENANT_ADMIN");
        var session = await SignInAsync(admin);

        var selfPromote = await session.SendAsync(HttpMethod.Put, $"/api/v1/staff/{adminMembership.TenantUserId}/roles",
            new { roles = new[] { "TENANT_OWNER" } });
        await selfPromote.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");

        var demoteOwner = await session.SendAsync(HttpMethod.Put, $"/api/v1/staff/{ownerMembership.TenantUserId}/roles",
            new { roles = new[] { "VIEW_ONLY" } });
        await demoteOwner.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Role_change_takes_effect_immediately_and_is_audited()
    {
        var (tenantId, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var viewer = await Data.CreateUserAsync();
        var viewerMembership = await Data.AddMembershipAsync(tenantId, viewer.UserId, "VIEW_ONLY");
        var ownerSession = await SignInAsync(owner);
        var viewerSession = await SignInAsync(viewer);
        (await viewerSession.GetAsync("/api/v1/staff")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var change = await ownerSession.SendAsync(HttpMethod.Put, $"/api/v1/staff/{viewerMembership.TenantUserId}/roles",
            new { roles = new[] { "MANAGER" } });
        change.StatusCode.Should().Be(HttpStatusCode.OK);

        (await viewerSession.GetAsync("/api/v1/staff")).StatusCode.Should().Be(HttpStatusCode.OK);
        var audit = await Data.QueryAsync<string>(
            "SELECT new_values::text FROM audit_logs WHERE tenant_id = @tenantId AND action = 'roles.changed'", new { tenantId });
        audit.Should().ContainSingle().Which.Should().Contain("MANAGER");
    }

    [Fact]
    public async Task Deactivated_staff_lose_access_immediately_and_cannot_refresh()
    {
        var (tenantId, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var host = await Data.CreateUserAsync();
        var hostMembership = await Data.AddMembershipAsync(tenantId, host.UserId, "HOST");
        var ownerSession = await SignInAsync(owner);
        var hostSession = await SignInAsync(host);
        (await hostSession.GetAsync("/api/v1/tenants/current")).StatusCode.Should().Be(HttpStatusCode.OK);

        var deactivate = await ownerSession.SendAsync(HttpMethod.Post, $"/api/v1/staff/{hostMembership.TenantUserId}/deactivate");
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deactivate.JsonAsync()).GetProperty("status").GetString().Should().Be("DISABLED");

        await (await hostSession.GetAsync("/api/v1/tenants/current")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await hostSession.RefreshAsync()).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");

        var reactivate = await ownerSession.SendAsync(HttpMethod.Post, $"/api/v1/staff/{hostMembership.TenantUserId}/activate");
        reactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await NewSession().LoginAsync(host.Email, host.Password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Roles_endpoint_lists_the_seeded_matrix()
    {
        var (_, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var session = await SignInAsync(owner);

        var roles = await (await session.GetAsync("/api/v1/roles")).JsonAsync();

        roles.EnumerateArray().Select(r => r.GetProperty("code").GetString())
            .Should().Equal("TENANT_OWNER", "TENANT_ADMIN", "MANAGER", "HOST", "STAFF", "VIEW_ONLY");
        roles[0].GetProperty("permissions").GetArrayLength().Should().Be(23);
        roles[5].GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().BeEquivalentTo("booking.view", "resource.view", "venue.view", "floorplan.view");
    }

    [Fact]
    public async Task Platform_tenant_management_is_limited_to_platform_admins()
    {
        var (_, owner, _) = await Data.CreateTenantWithUserAsync("TENANT_OWNER");
        var platformAdmin = await Data.CreateUserAsync(platformAdmin: true);
        var ownerSession = await SignInAsync(owner);
        var adminSession = await SignInAsync(platformAdmin);
        var request = new
        {
            name = "Fresh Bistro",
            slug = "fresh-bistro-" + Guid.NewGuid().ToString("N")[..6],
            defaultTimeZone = "Africa/Johannesburg",
            owner = new { email = $"owner-{Guid.NewGuid():N}@example.test", displayName = "Fresh Owner", password = "Fresh-bistro-owner-1" },
        };

        await (await ownerSession.GetAsync("/api/v1/platform/tenants")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await ownerSession.SendAsync(HttpMethod.Post, "/api/v1/platform/tenants", request)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");

        var created = await adminSession.SendAsync(HttpMethod.Post, "/api/v1/platform/tenants", request);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var duplicate = await adminSession.SendAsync(HttpMethod.Post, "/api/v1/platform/tenants", request);
        await duplicate.ShouldBeProblemAsync(HttpStatusCode.Conflict, "SLUG_TAKEN");

        var newOwner = await NewSession().LoginOkAsync(request.owner.email, request.owner.password);
        newOwner.LastAuthBody.GetProperty("memberships")[0].GetProperty("roles")[0].GetString().Should().Be("TENANT_OWNER");

        // Platform admins have no implicit tenant data access.
        await (await adminSession.GetAsync("/api/v1/staff")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "TENANT_SELECTION_REQUIRED");
    }
}

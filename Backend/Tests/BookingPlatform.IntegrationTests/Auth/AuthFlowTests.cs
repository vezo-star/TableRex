using System.Net;
using System.Text.Json;
using BookingPlatform.IntegrationTests.Infrastructure;

namespace BookingPlatform.IntegrationTests.Auth;

public sealed class AuthFlowTests(InfrastructureFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Login_with_single_membership_returns_tenant_scoped_token_and_secure_refresh_cookie()
    {
        var (tenantId, user, _) = await Data.CreateTenantWithUserAsync("MANAGER");
        var session = NewSession();

        var response = await session.LoginAsync(user.Email.ToUpperInvariant(), user.Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = session.LastAuthBody;
        body.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        body.GetProperty("requiresTenantSelection").GetBoolean().Should().BeFalse();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("bp_refresh=", StringComparison.Ordinal));
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/api/v1/auth");

        var me = await (await session.GetAsync("/api/v1/auth/me")).JsonAsync();
        me.GetProperty("email").GetString().Should().Be(user.Email);
        me.GetProperty("currentTenant").GetProperty("tenantId").GetGuid().Should().Be(tenantId);
        me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("MANAGER");
        var permissions = me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        permissions.Should().Contain(["booking.create", "staff.view", "floorplan.manage"]).And.NotContain(["venue.manage", "staff.manage", "settings.manage"]);
        me.GetProperty("hasAllVenueAccess").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_return_the_same_generic_error()
    {
        var (_, user, _) = await Data.CreateTenantWithUserAsync();

        var wrongPassword = await NewSession().LoginAsync(user.Email, "not-the-right-password");
        var unknownUser = await NewSession().LoginAsync("nobody-" + Guid.NewGuid().ToString("N") + "@example.test", "whatever-password");

        await wrongPassword.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        await unknownUser.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        var a = await wrongPassword.JsonAsync();
        var b = await unknownUser.JsonAsync();
        a.GetProperty("detail").GetString().Should().Be(b.GetProperty("detail").GetString());

        var failures = await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE action = 'auth.login.failed'");
        failures.Should().Be(2);
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures_even_for_the_correct_password()
    {
        var (_, user, _) = await Data.CreateTenantWithUserAsync();

        for (var i = 0; i < 5; i++)
        {
            await (await NewSession().LoginAsync(user.Email, "wrong-password-" + i)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        }

        var locked = await NewSession().LoginAsync(user.Email, user.Password);

        await locked.ShouldBeProblemAsync(HttpStatusCode.Locked, "ACCOUNT_LOCKED");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE action = 'auth.account.locked' AND entity_id = @id", new { id = user.UserId }))
            .Should().Be(1);
    }

    [Fact]
    public async Task Refresh_rotates_and_reusing_a_rotated_token_revokes_the_family()
    {
        var (_, user, _) = await Data.CreateTenantWithUserAsync();
        var session = await SignInAsync(user);
        var original = session.RefreshToken!;

        var rotated = await session.RefreshAsync();
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = session.RefreshToken!;
        second.Should().NotBe(original);

        var reuse = await session.RefreshAsync(original);
        await reuse.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");

        var afterTheft = await session.RefreshAsync(second);
        await afterTheft.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE action = 'auth.refresh.reuse_detected'")).Should().Be(1);
    }

    [Fact]
    public async Task Refresh_tokens_are_stored_only_as_hashes()
    {
        var (_, user, _) = await Data.CreateTenantWithUserAsync();
        var session = await SignInAsync(user);

        var stored = await Data.QueryAsync<byte[]>("SELECT token_hash FROM refresh_tokens WHERE user_id = @id", new { id = user.UserId });

        stored.Should().ContainSingle().Which.Should().Equal(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(session.RefreshToken!)));
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE new_values::text LIKE @t OR old_values::text LIKE @t", new { t = $"%{session.RefreshToken}%" }))
            .Should().Be(0);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_family_and_clears_the_cookie()
    {
        var (_, user, _) = await Data.CreateTenantWithUserAsync();
        var session = await SignInAsync(user);
        var token = session.RefreshToken!;

        var logout = await session.PostWithCookieAsync("/api/v1/auth/logout");

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        logout.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("bp_refresh=;", StringComparison.Ordinal));
        await (await session.RefreshAsync(token)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");
        (await Data.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE action = 'auth.logout' AND entity_id = @id", new { id = user.UserId }))
            .Should().Be(1);
    }

    [Fact]
    public async Task Refresh_from_a_foreign_origin_is_rejected()
    {
        var (_, user, _) = await Data.CreateTenantWithUserAsync();
        var session = await SignInAsync(user);

        var foreign = await session.RefreshAsync(origin: "https://evil.example");
        var allowed = await session.RefreshAsync(origin: ApiFactory.AllowedOrigin);

        await foreign.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Multi_tenant_user_must_select_a_tenant_and_can_only_select_own_memberships()
    {
        var user = await Data.CreateUserAsync();
        var tenantA = await Data.CreateTenantAsync(name: "Alpha");
        var tenantB = await Data.CreateTenantAsync(name: "Bravo");
        var foreignTenant = await Data.CreateTenantAsync(name: "Foreign");
        await Data.AddMembershipAsync(tenantA, user.UserId, "TENANT_ADMIN");
        await Data.AddMembershipAsync(tenantB, user.UserId, "VIEW_ONLY");
        var session = await SignInAsync(user);

        session.LastAuthBody.GetProperty("requiresTenantSelection").GetBoolean().Should().BeTrue();
        session.LastAuthBody.GetProperty("tenantId").ValueKind.Should().Be(JsonValueKind.Null);
        session.LastAuthBody.GetProperty("memberships").GetArrayLength().Should().Be(2);
        await (await session.GetAsync("/api/v1/tenants/current")).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "TENANT_SELECTION_REQUIRED");

        var toForeign = await session.PostWithCookieAsync("/api/v1/auth/switch-tenant", new { tenantId = foreignTenant });
        await toForeign.ShouldBeProblemAsync(HttpStatusCode.NotFound, "TENANT_NOT_FOUND");

        var toB = await session.PostWithCookieAsync("/api/v1/auth/switch-tenant", new { tenantId = tenantB });
        toB.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = await (await session.GetAsync("/api/v1/tenants/current")).JsonAsync();
        current.GetProperty("id").GetGuid().Should().Be(tenantB);
        current.GetProperty("name").GetString().Should().Be("Bravo");

        // Refresh keeps the selected tenant.
        (await session.RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        session.LastAuthBody.GetProperty("tenantId").GetGuid().Should().Be(tenantB);
    }

    [Fact]
    public async Task Change_password_revokes_other_sessions_and_the_new_password_works()
    {
        var (_, user, _) = await Data.CreateTenantWithUserAsync();
        var other = await SignInAsync(user);
        var session = await SignInAsync(user);

        var weak = await session.PostWithCookieAsync("/api/v1/auth/change-password", new { currentPassword = user.Password, newPassword = "short" });
        await weak.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var wrongCurrent = await session.PostWithCookieAsync("/api/v1/auth/change-password",
            new { currentPassword = "wrong-current-password", newPassword = "A-much-better-passphrase-42" });
        await wrongCurrent.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var changed = await session.PostWithCookieAsync("/api/v1/auth/change-password",
            new { currentPassword = user.Password, newPassword = "A-much-better-passphrase-42" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        await (await other.RefreshAsync()).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");
        (await session.RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        await (await NewSession().LoginAsync(user.Email, user.Password)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        (await NewSession().LoginAsync(user.Email, "A-much-better-passphrase-42")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Disabled_user_cannot_log_in_or_use_existing_tokens()
    {
        var (_, user, _) = await Data.CreateTenantWithUserAsync();
        var session = await SignInAsync(user);
        await Data.ExecuteAsync("UPDATE users SET status = 'DISABLED' WHERE id = @id", new { id = user.UserId });

        await (await NewSession().LoginAsync(user.Email, user.Password)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        await (await session.RefreshAsync()).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Protected_endpoints_require_a_valid_token()
    {
        var anonymous = NewSession();
        await (await anonymous.GetAsync("/api/v1/auth/me")).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHORIZED");

        anonymous.UseAccessToken("eyJhbGciOiJub25lIn0.eyJzdWIiOiJ4In0.");
        await (await anonymous.GetAsync("/api/v1/staff")).ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Fact]
    public async Task Login_is_rate_limited_per_client()
    {
        HttpResponseMessage? last = null;
        for (var i = 0; i < 21; i++)
        {
            last = await NewSession().LoginAsync($"rl-{i}@example.test", "irrelevant-password");
        }

        await last!.ShouldBeProblemAsync(HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        last!.Headers.RetryAfter.Should().NotBeNull();
    }
}

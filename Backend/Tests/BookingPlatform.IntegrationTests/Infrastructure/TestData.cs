using BookingPlatform.Infrastructure.Security;
using Dapper;
using Npgsql;

namespace BookingPlatform.IntegrationTests.Infrastructure;

public sealed record TestUser(Guid UserId, string Email, string Password);

public sealed record TestMembership(Guid TenantId, Guid TenantUserId);

/// <summary>Direct SQL seeding for integration tests (bypasses the API so tests control exact state).</summary>
public sealed class TestData(string connectionString)
{
    public const string DefaultPassword = "Correct-Horse-Battery-9";

    // Hashing is deliberately slow (PBKDF2 210k); share one hash for the default password.
    private static readonly Lazy<string> DefaultHash = new(() => new PasswordHasherAdapter().Hash(DefaultPassword));

    public string ConnectionString => connectionString;

    /// <param name="plan">Test tenants default to the unlimited plan so plan limits only apply where a test opts in.</param>
    public async Task<Guid> CreateTenantAsync(string? slug = null, string name = "Test Tenant", string timeZone = "Africa/Johannesburg", string? plan = "ENTERPRISE")
    {
        slug ??= "t-" + Guid.NewGuid().ToString("N")[..12];
        await using var connection = new NpgsqlConnection(connectionString);
        var tenantId = await connection.ExecuteScalarAsync<Guid>(
            "INSERT INTO tenants (name, slug, default_time_zone) VALUES (@name, @slug, @timeZone) RETURNING id",
            new { name, slug, timeZone });
        if (plan is not null)
        {
            await connection.ExecuteAsync("INSERT INTO tenant_subscriptions (tenant_id, plan_code) VALUES (@tenantId, @plan)", new { tenantId, plan });
        }

        return tenantId;
    }

    public async Task<TestUser> CreateUserAsync(string? email = null, bool platformAdmin = false, string? password = null)
    {
        email ??= $"user-{Guid.NewGuid():N}@example.test";
        var hash = password is null ? DefaultHash.Value : new PasswordHasherAdapter().Hash(password);
        await using var connection = new NpgsqlConnection(connectionString);
        var id = await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO users (email, password_hash, display_name, is_platform_admin)
            VALUES (@email, @hash, @name, @platformAdmin) RETURNING id
            """,
            new { email, hash, name = "Test " + email.Split('@')[0], platformAdmin });
        return new TestUser(id, email, password ?? DefaultPassword);
    }

    public async Task<TestMembership> AddMembershipAsync(Guid tenantId, Guid userId, params string[] roles)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        var tenantUserId = await connection.ExecuteScalarAsync<Guid>(
            "INSERT INTO tenant_users (tenant_id, user_id) VALUES (@tenantId, @userId) RETURNING id",
            new { tenantId, userId });
        await connection.ExecuteAsync(
            """
            INSERT INTO tenant_user_roles (tenant_id, tenant_user_id, role_id)
            SELECT @tenantId, @tenantUserId, id FROM roles WHERE tenant_id IS NULL AND code = ANY(@roles)
            """,
            new { tenantId, tenantUserId, roles });
        return new TestMembership(tenantId, tenantUserId);
    }

    /// <summary>Creates a tenant with one user holding <paramref name="roles"/>.</summary>
    public async Task<(Guid TenantId, TestUser User, TestMembership Membership)> CreateTenantWithUserAsync(params string[] roles)
    {
        var tenantId = await CreateTenantAsync();
        var user = await CreateUserAsync();
        var membership = await AddMembershipAsync(tenantId, user.UserId, roles.Length == 0 ? ["TENANT_OWNER"] : roles);
        return (tenantId, user, membership);
    }

    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return (await connection.QueryAsync<T>(sql, parameters)).AsList();
    }

    public async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync(sql, parameters);
    }
}

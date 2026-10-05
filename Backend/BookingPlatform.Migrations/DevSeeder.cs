using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BookingPlatform.Migrations;

public sealed record DevSeedOptions(string AdminEmail, string AdminPassword);

/// <summary>
/// Idempotent demo data for local development (D-016). Never runs in Production; never part of migrations.
/// Existing users keep their passwords; re-running only fills in what is missing.
/// </summary>
public sealed class DevSeeder(ILogger<DevSeeder> logger)
{
    public const string DemoTenantSlug = "demo-hospitality";
    public const string SecondTenantSlug = "demo-coworking";

    // Must match BookingPlatform.Infrastructure.Security.PasswordHasherAdapter.
    private const int HashIterations = 210_000;

    private static readonly (string LocalPart, string Name, string Role)[] DemoStaff =
    [
        ("manager", "Demo Manager", "MANAGER"),
        ("host", "Demo Host", "HOST"),
        ("staff", "Demo Staff", "STAFF"),
        ("viewer", "Demo Viewer", "VIEW_ONLY"),
    ];

    public async Task SeedAsync(string connectionString, DevSeedOptions options, CancellationToken cancellationToken)
    {
        var hasher = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = HashIterations,
        }));
        var passwordHash = hasher.HashPassword(new object(), options.AdminPassword);
        var domain = options.AdminEmail.Split('@')[1];

        await using var dataSource = NpgsqlDataSource.Create(PostgresConnectionDefaults.Apply(connectionString));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var demoTenantId = await EnsureTenantAsync(connection, transaction, "Demo Hospitality", DemoTenantSlug, cancellationToken);
        var secondTenantId = await EnsureTenantAsync(connection, transaction, "Demo Coworking", SecondTenantSlug, cancellationToken);

        var adminId = await EnsureUserAsync(connection, transaction, options.AdminEmail, "Dev Admin", passwordHash, true, cancellationToken);
        await EnsureMembershipAsync(connection, transaction, demoTenantId, adminId, "TENANT_OWNER", cancellationToken);
        await EnsureMembershipAsync(connection, transaction, secondTenantId, adminId, "TENANT_OWNER", cancellationToken);

        var staffMemberships = new Dictionary<string, Guid>();
        foreach (var (localPart, name, role) in DemoStaff)
        {
            var userId = await EnsureUserAsync(connection, transaction, $"{localPart}@{domain}", name, passwordHash, false, cancellationToken);
            staffMemberships[localPart] = await EnsureMembershipAsync(connection, transaction, demoTenantId, userId, role, cancellationToken);
        }

        var db = new SeedDb(connection, transaction, cancellationToken);
        var restaurantId = await DemoVenues.EnsureRestaurantAsync(db, demoTenantId);
        var clubId = await DemoVenues.EnsureClubAsync(db, demoTenantId);
        await DemoVenues.EnsureWorkspaceAsync(db, secondTenantId);

        foreach (var localPart in new[] { "manager", "host", "staff", "viewer" })
        {
            await DemoVenues.AssignAsync(db, demoTenantId, staffMemberships[localPart], restaurantId);
        }

        await DemoVenues.AssignAsync(db, demoTenantId, staffMemberships["manager"], clubId);

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Dev seed complete: tenants {DemoTenant} and {SecondTenant} with demo venues; admin {AdminEmail}; staff manager/host/staff/viewer@{Domain}",
            DemoTenantSlug, SecondTenantSlug, options.AdminEmail, domain);
    }

    private static async Task<Guid> EnsureTenantAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string name, string slug, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            WITH tenant AS (
                INSERT INTO tenants (name, slug, default_time_zone) VALUES (@name, @slug, 'Africa/Johannesburg')
                ON CONFLICT (slug) DO UPDATE SET slug = EXCLUDED.slug
                RETURNING id),
            subscription AS (
                INSERT INTO tenant_subscriptions (tenant_id, plan_code) SELECT id, 'PROFESSIONAL' FROM tenant
                ON CONFLICT (tenant_id) DO NOTHING)
            SELECT id FROM tenant
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("slug", slug);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<Guid> EnsureUserAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string email, string displayName, string passwordHash,
        bool isPlatformAdmin, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users (email, password_hash, display_name, is_platform_admin)
            VALUES (lower(@email)::citext, @hash, @name, @admin)
            ON CONFLICT (email) DO UPDATE SET email = EXCLUDED.email
            RETURNING id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("hash", passwordHash);
        command.Parameters.AddWithValue("name", displayName);
        command.Parameters.AddWithValue("admin", isPlatformAdmin);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<Guid> EnsureMembershipAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid userId, string roleCode, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            WITH membership AS (
                INSERT INTO tenant_users (tenant_id, user_id) VALUES (@tenantId, @userId)
                ON CONFLICT (tenant_id, user_id) DO UPDATE SET tenant_id = EXCLUDED.tenant_id
                RETURNING id
            ),
            role_link AS (
                INSERT INTO tenant_user_roles (tenant_id, tenant_user_id, role_id)
                SELECT @tenantId, m.id, r.id FROM membership m CROSS JOIN roles r
                WHERE r.tenant_id IS NULL AND r.code = @role
                ON CONFLICT DO NOTHING
            )
            SELECT id FROM membership
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("role", roleCode);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}

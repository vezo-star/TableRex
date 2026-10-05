using BookingPlatform.IntegrationTests.Infrastructure;
using BookingPlatform.Migrations;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace BookingPlatform.IntegrationTests.Database;

[Collection(InfrastructureCollection.Name)]
[Trait("Category", "Docker")]
public sealed class AppRoleProvisionerTests(InfrastructureFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task App_role_can_read_and_write_data_but_not_change_the_schema_and_follows_password_rotation()
    {
        var ownerConnectionString = await fixture.CreateMigratedDatabaseAsync();
        var provisioner = new AppRoleProvisioner(NullLogger<AppRoleProvisioner>.Instance);
        var roleName = $"app_{Guid.NewGuid():N}"[..20];
        var options = new AppRoleOptions(roleName, "Role-Test-Value-Alpha-913");

        await provisioner.RunAsync(ownerConnectionString, options, Ct);

        await using (var owner = new NpgsqlConnection(ownerConnectionString))
        {
            var flags = await owner.QuerySingleAsync<(bool Super, bool BypassRls, bool CreateRole, bool CreateDb, bool Login)>(
                "SELECT rolsuper, rolbypassrls, rolcreaterole, rolcreatedb, rolcanlogin FROM pg_roles WHERE rolname = @roleName",
                new { roleName });
            flags.Should().Be((false, false, false, false, true));
            var stored = await owner.QuerySingleAsync<string>(
                "SELECT rolpassword FROM pg_authid WHERE rolname = @roleName", new { roleName });
            stored.Should().StartWith("SCRAM-SHA-256$4096:");
        }

        await using (var app = new NpgsqlConnection(As(ownerConnectionString, options)))
        {
            await app.OpenAsync(Ct);
            await app.ExecuteAsync(
                "INSERT INTO users (email, password_hash, display_name) VALUES ('app-role@example.test', 'hash', 'App Role')");
            (await app.ExecuteScalarAsync<int>("SELECT count(*) FROM users")).Should().Be(1);
            (await app.ExecuteAsync("DELETE FROM users")).Should().Be(1);
            (await app.ExecuteScalarAsync<int>("SELECT count(*) FROM schema_versions")).Should().BeGreaterThan(30);

            foreach (var forbidden in new[]
            {
                "CREATE TABLE app_role_probe (id int)",
                "DROP TABLE bookings",
                "ALTER TABLE bookings DISABLE TRIGGER ALL",
                "TRUNCATE audit_logs",
                "DELETE FROM schema_versions",
            })
            {
                var act = () => app.ExecuteAsync(forbidden);
                (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().BeOneOf("42501", "42P01");
            }
        }

        var rotated = options with { Password = "Role-Test-Value-Bravo-427" };
        await provisioner.RunAsync(ownerConnectionString, rotated, Ct);

        await using (var stale = new NpgsqlConnection(As(ownerConnectionString, options)))
        {
            var act = () => stale.OpenAsync(Ct);
            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("28P01");
        }

        await using var current = new NpgsqlConnection(As(ownerConnectionString, rotated));
        await current.OpenAsync(Ct);
        (await current.ExecuteScalarAsync<string>("SELECT current_user")).Should().Be(roleName);
    }

    [Fact]
    public async Task Refuses_to_reconfigure_the_owner_role()
    {
        var ownerConnectionString = await fixture.CreateMigratedDatabaseAsync();
        var owner = new NpgsqlConnectionStringBuilder(ownerConnectionString).Username!;
        var provisioner = new AppRoleProvisioner(NullLogger<AppRoleProvisioner>.Instance);

        var act = () => provisioner.RunAsync(ownerConnectionString, new AppRoleOptions(owner, "Role-Test-Value-Alpha-913"), Ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*must differ*");
    }

    [Theory]
    [InlineData("App_User", "Role-Test-Value-Alpha-913", "APP_DB_USER")]
    [InlineData("1app", "Role-Test-Value-Alpha-913", "APP_DB_USER")]
    [InlineData("app\"; DROP ROLE x; --", "Role-Test-Value-Alpha-913", "APP_DB_USER")]
    [InlineData("app_user", "too-short-1", "16-128")]
    [InlineData("app_user", "has a space in it 12345", "16-128")]
    [InlineData("app_user", "Pässwörd-mit-Umlauten-1", "16-128")]
    [InlineData("app_user", "abababababababababab", "predictable")]
    [InlineData("app_user", "CHANGE_ME_app_database_password", "placeholder")]
    [InlineData("app_user", "Role-Test;Value-Alpha-913", "connection strings")]
    [InlineData("app_user", "Role-Test'Value-Alpha-913", "connection strings")]
    public void Rejects_unsafe_options(string user, string password, string expected) =>
        AppRoleProvisioner.Validate(new AppRoleOptions(user, password)).Should().Contain(expected);

    [Fact]
    public void Accepts_reasonable_options() =>
        AppRoleProvisioner.Validate(new AppRoleOptions("bookingplatform_app", "Role-Test-Value-Alpha-913")).Should().BeNull();

    private static string As(string connectionString, AppRoleOptions options) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            Username = options.UserName,
            Password = options.Password,
            Pooling = false,
        }.ConnectionString;
}

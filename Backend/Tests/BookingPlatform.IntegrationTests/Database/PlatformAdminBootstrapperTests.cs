using BookingPlatform.Application.Identity;
using BookingPlatform.Infrastructure.Security;
using BookingPlatform.IntegrationTests.Infrastructure;
using BookingPlatform.Migrations;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace BookingPlatform.IntegrationTests.Database;

[Collection(InfrastructureCollection.Name)]
[Trait("Category", "Docker")]
public sealed class PlatformAdminBootstrapperTests(InfrastructureFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creates_the_admin_once_and_promotes_existing_accounts_without_touching_their_password()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        var bootstrapper = new PlatformAdminBootstrapper(NullLogger<PlatformAdminBootstrapper>.Instance);
        var options = new PlatformAdminOptions("Ops@Example.test", "Bootstrap-Test-Value-77", "Ops Team");

        (await bootstrapper.RunAsync(connectionString, options, Ct)).Should().Be(PlatformAdminOutcome.Created);
        (await bootstrapper.RunAsync(connectionString, options with { Password = "Another-Value-Ignored-1" }, Ct))
            .Should().Be(PlatformAdminOutcome.AlreadyAdmin);

        await using var connection = new NpgsqlConnection(connectionString);
        var admin = await connection.QuerySingleAsync<(string Email, string Hash, bool IsPlatformAdmin, string DisplayName)>(
            "SELECT email::text, password_hash, is_platform_admin, display_name FROM users");
        admin.Email.Should().Be("ops@example.test");
        admin.IsPlatformAdmin.Should().BeTrue();
        admin.DisplayName.Should().Be("Ops Team");
        new PasswordHasherAdapter().Verify(admin.Hash, "Bootstrap-Test-Value-77").Should().Be(PasswordVerification.Success);

        await connection.ExecuteAsync(
            "INSERT INTO users (email, password_hash, display_name) VALUES ('staff@example.test', 'existing-hash', 'Staff')");
        (await bootstrapper.RunAsync(connectionString, options with { Email = "STAFF@example.test" }, Ct))
            .Should().Be(PlatformAdminOutcome.Promoted);
        var promoted = await connection.QuerySingleAsync<(string Hash, bool IsPlatformAdmin)>(
            "SELECT password_hash, is_platform_admin FROM users WHERE email = 'staff@example.test'");
        promoted.Should().Be(("existing-hash", true));
    }

    [Theory]
    [InlineData("not-an-email", "Bootstrap-Test-Value-77", "Ops", "valid email")]
    [InlineData("a@b@c.test", "Bootstrap-Test-Value-77", "Ops", "valid email")]
    [InlineData("ops@example.test", "short", "Ops", "12-128")]
    [InlineData("ops@example.test", "aaaaaaaaaaaaaaaa", "Ops", "predictable")]
    [InlineData("operations@example.test", "my-operations-pass-1", "Ops", "predictable")]
    [InlineData("ops@example.test", "Bootstrap-Test-Value-77", " ", "PLATFORM_ADMIN_NAME")]
    public void Rejects_unsafe_options(string email, string password, string name, string expected)
    {
        PlatformAdminBootstrapper.Validate(new PlatformAdminOptions(email, password, name)).Should().Contain(expected);
    }

    [Fact]
    public void Accepts_reasonable_options() =>
        PlatformAdminBootstrapper.Validate(new PlatformAdminOptions("ops@example.test", "Bootstrap-Test-Value-77", "Ops")).Should().BeNull();
}

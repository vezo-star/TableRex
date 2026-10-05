using BookingPlatform.IntegrationTests.Infrastructure;
using BookingPlatform.Migrations;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace BookingPlatform.IntegrationTests.Database;

[Collection(InfrastructureCollection.Name)]
[Trait("Category", "Docker")]
public sealed class DevSeederTests(InfrastructureFixture fixture)
{
    [Fact]
    public async Task Seeds_demo_data_once_and_rerunning_changes_nothing()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        var seeder = new DevSeeder(NullLogger<DevSeeder>.Instance);
        var options = new DevSeedOptions("admin@seed.test", "Seeder-Test-Value-42");

        await seeder.SeedAsync(connectionString, options, TestContext.Current.CancellationToken);
        var first = await CountsAsync(connectionString);
        await seeder.SeedAsync(connectionString, options, TestContext.Current.CancellationToken);
        var second = await CountsAsync(connectionString);

        first.Venues.Should().Be(3);
        first.Resources.Should().BeGreaterThan(20);
        first.Combinations.Should().Be(1);
        first.Members.Should().Be(2);
        second.Should().Be(first);
    }

    private sealed record Counts(long Venues, long Resources, long Combinations, long Members);

    private static async Task<Counts> CountsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        return await connection.QuerySingleAsync<Counts>(
            """
            SELECT (SELECT count(*) FROM venues) AS venues,
                   (SELECT count(*) FROM bookable_resources) AS resources,
                   (SELECT count(*) FROM resource_combinations) AS combinations,
                   (SELECT count(*) FROM resource_combination_members) AS members
            """);
    }
}

using BookingPlatform.IntegrationTests.Infrastructure;
using BookingPlatform.Migrations;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace BookingPlatform.IntegrationTests.Database;

[Collection(InfrastructureCollection.Name)]
[Trait("Category", "Docker")]
public sealed class MigrationTests(InfrastructureFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task All_migrations_apply_to_an_empty_database_and_rerunning_is_a_no_op()
    {
        var connectionString = await fixture.CreateEmptyDatabaseAsync();
        var runner = new MigrationRunner(NullLogger<MigrationRunner>.Instance);
        var expected = MigrationScripts.LoadEmbedded().Select(script => script.Name).ToArray();

        var first = runner.Run(connectionString);

        first.Successful.Should().BeTrue(first.Error?.ToString());
        first.AppliedScripts.Should().Equal(expected);

        var second = runner.Run(connectionString);

        second.Successful.Should().BeTrue(second.Error?.ToString());
        second.AppliedScripts.Should().BeEmpty();

        await using var connection = new NpgsqlConnection(connectionString);
        var journal = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT scriptname FROM public.schema_versions ORDER BY schemaversionsid", cancellationToken: Ct));
        journal.Should().Equal(expected);
    }

    [Fact]
    public async Task Required_extensions_are_installed()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        var extensions = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT extname FROM pg_extension", cancellationToken: Ct));

        extensions.Should().Contain(["btree_gist", "pg_trgm", "citext"]);
    }

    [Fact]
    public async Task Server_is_postgresql_18()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        var versionNumber = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT current_setting('server_version_num')::int", cancellationToken: Ct));

        versionNumber.Should().BeGreaterThanOrEqualTo(180000).And.BeLessThan(190000);
    }
}

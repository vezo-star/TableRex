using BookingPlatform.Migrations;

namespace BookingPlatform.UnitTests.Migrations;

public sealed class MigrationScriptsTests
{
    [Fact]
    public void Embedded_scripts_load_in_version_order_starting_with_extensions()
    {
        var scripts = MigrationScripts.LoadEmbedded();

        scripts.Should().NotBeEmpty();
        scripts[0].Name.Should().Be("V001__extensions.sql");
        scripts.Select(script => script.Version).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        scripts.Select(script => script.Name).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void Extensions_migration_enables_required_extensions()
    {
        var extensions = MigrationScripts.LoadEmbedded().Single(script => script.Version == 1);

        extensions.Contents.Should().Contain("btree_gist").And.Contain("pg_trgm").And.Contain("citext");
    }

    [Fact]
    public void Validate_accepts_conventional_names() =>
        MigrationScripts.Validate(["V001__extensions.sql", "V002__tenants.sql", "V010__floor_plans.sql"]).Should().BeEmpty();

    [Theory]
    [InlineData("V1__extensions.sql")]
    [InlineData("V001_extensions.sql")]
    [InlineData("V001__Extensions.sql")]
    [InlineData("001__extensions.sql")]
    [InlineData("V001__extensions.txt")]
    [InlineData("V001__extensions-and-more.sql")]
    public void Validate_rejects_unconventional_names(string fileName) =>
        MigrationScripts.Validate([fileName]).Should().ContainSingle().Which.Should().Contain(fileName);

    [Fact]
    public void Validate_rejects_duplicate_versions() =>
        MigrationScripts.Validate(["V002__tenants.sql", "V002__users.sql"])
            .Should().ContainSingle().Which.Should().Contain("V002");

    [Fact]
    public void Validate_rejects_an_empty_set() =>
        MigrationScripts.Validate([]).Should().ContainSingle();
}

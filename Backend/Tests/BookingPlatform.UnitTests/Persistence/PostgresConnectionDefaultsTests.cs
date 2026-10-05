using Npgsql;
using InfrastructureDefaults = BookingPlatform.Infrastructure.Persistence.PostgresConnectionDefaults;
using MigrationsDefaults = BookingPlatform.Migrations.PostgresConnectionDefaults;

namespace BookingPlatform.UnitTests.Persistence;

public sealed class PostgresConnectionDefaultsTests
{
    private const string Base = "Host=db;Port=5432;Database=app;Username=app;Password=secret";

    public static TheoryData<string, GssEncryptionMode> ExplicitSettings => new()
    {
        { $"{Base};GSS Encryption Mode=Require", GssEncryptionMode.Require },
        { $"{Base};gss encryption mode=Prefer", GssEncryptionMode.Prefer },
        { $"{Base};GssEncryptionMode=Prefer", GssEncryptionMode.Prefer },
    };

    [Fact]
    public void Migrations_defaults_disable_gss_encryption_when_not_configured()
    {
        var result = new NpgsqlConnectionStringBuilder(MigrationsDefaults.Apply(Base));

        result.GssEncryptionMode.Should().Be(GssEncryptionMode.Disable);
        result.Host.Should().Be("db");
        result.Password.Should().Be("secret");
    }

    [Theory]
    [MemberData(nameof(ExplicitSettings))]
    public void Migrations_defaults_keep_an_explicit_gss_setting(string connectionString, GssEncryptionMode expected)
    {
        var result = new NpgsqlConnectionStringBuilder(MigrationsDefaults.Apply(connectionString));

        result.GssEncryptionMode.Should().Be(expected);
    }

    [Fact]
    public void Infrastructure_defaults_disable_gss_encryption_when_not_configured()
    {
        var builder = new NpgsqlConnectionStringBuilder(Base);

        InfrastructureDefaults.Apply(builder, Base);

        builder.GssEncryptionMode.Should().Be(GssEncryptionMode.Disable);
    }

    [Theory]
    [MemberData(nameof(ExplicitSettings))]
    public void Infrastructure_defaults_keep_an_explicit_gss_setting(string connectionString, GssEncryptionMode expected)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        InfrastructureDefaults.Apply(builder, connectionString);

        builder.GssEncryptionMode.Should().Be(expected);
    }
}

using BookingPlatform.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace BookingPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// Starts PostgreSQL 18 and Redis 8 containers once per test collection. Migrations are applied once to a template
/// database; each test gets its own database cloned from it (or an empty one when testing migrations themselves).
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:18-alpine";
    public const string RedisImage = "redis:8-alpine";
    private const string TemplateDatabase = "booking_template";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresImage).Build();
    private readonly RedisContainer _redis = new RedisBuilder(RedisImage).Build();

    public string RedisConnectionString => _redis.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        await ExecuteOnServerAsync($"CREATE DATABASE {TemplateDatabase}");

        // Pooling is disabled so no idle connection keeps the template busy; PostgreSQL refuses to clone a template in use.
        var result = new MigrationRunner(NullLogger<MigrationRunner>.Instance).Run(ConnectionStringFor(TemplateDatabase, pooling: false));
        if (!result.Successful)
        {
            throw new InvalidOperationException("Migrating the template database failed.", result.Error);
        }
    }

    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = NewDatabaseName();
        await ExecuteOnServerAsync($"CREATE DATABASE {name}");
        return ConnectionStringFor(name);
    }

    public async Task<string> CreateMigratedDatabaseAsync()
    {
        var name = NewDatabaseName();
        await ExecuteOnServerAsync($"CREATE DATABASE {name} TEMPLATE {TemplateDatabase}");
        return ConnectionStringFor(name);
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private string ConnectionStringFor(string database, bool pooling = true) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database, Pooling = pooling }.ConnectionString;

    private static string NewDatabaseName() => "test_" + Guid.NewGuid().ToString("N");

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor("postgres", pooling: false));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}

using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace BookingPlatform.Infrastructure.HealthChecks;

public sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: cancellationToken));
            return result == 1
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("PostgreSQL returned an unexpected result.");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            // The exception is summarised rather than attached: readiness probes run every few seconds and a stack trace per probe floods logs.
            return new HealthCheckResult(context.Registration.FailureStatus, $"PostgreSQL is unreachable ({exception.GetType().Name}: {exception.Message})");
        }
    }
}

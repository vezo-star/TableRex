using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace BookingPlatform.Infrastructure.HealthChecks;

public sealed class RedisHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!multiplexer.IsConnected)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis is not connected.");
        }

        try
        {
            await multiplexer.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"Redis is unreachable ({exception.GetType().Name}: {exception.Message})");
        }
    }
}

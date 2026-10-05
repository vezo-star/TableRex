using System.Text.Json;
using BookingPlatform.Infrastructure.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BookingPlatform.Api.Health;

public static class HealthEndpoints
{
    public const string LivenessPath = "/health";
    public const string ReadinessPath = "/health/ready";
    public const string ReadyTag = "ready";

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddPlatformHealthChecks(this IServiceCollection services)
    {
        // Redis holds no authoritative data and the Api keeps serving without it (in-memory rate-limit fallback), so an
        // outage is Degraded (still HTTP 200): a load balancer must not pull every instance out of rotation for it.
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", HealthStatus.Unhealthy, [ReadyTag], CheckTimeout)
            .AddCheck<RedisHealthCheck>("redis", HealthStatus.Degraded, [ReadyTag], CheckTimeout);

        return services;
    }

    public static IEndpointRouteBuilder MapPlatformHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: the process is up and serving requests. No dependency checks, so a database outage never restarts pods.
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponse,
        }).AllowAnonymous();

        endpoints.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponse,
        }).AllowAnonymous();

        return endpoints;
    }

    // Deliberately exposes only names and statuses: no exception messages, hosts or configuration.
    private static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}

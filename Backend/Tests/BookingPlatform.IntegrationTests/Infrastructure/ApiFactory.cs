using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookingPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real Api in the Test environment with explicit dependency connection strings.
/// Test-only controllers in this assembly are added as an application part.
/// </summary>
public sealed class ApiFactory(string postgresConnectionString, string redisConnectionString) : WebApplicationFactory<Program>
{
    // Ports that refuse connections immediately, for tests that need dependencies to be down.
    public const string UnreachablePostgres = "Host=127.0.0.1;Port=1;Database=unreachable;Username=unreachable;Password=unreachable;Timeout=2";
    public const string UnreachableRedis = "127.0.0.1:1,abortConnect=false,connectTimeout=1000";

    public static ApiFactory WithUnreachableDependencies() => new(UnreachablePostgres, UnreachableRedis);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = postgresConnectionString,
            ["ConnectionStrings:Redis"] = redisConnectionString,
            ["Cors:AllowedOrigins:0"] = "http://localhost:9000",
        }));

        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly));
    }
}

using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookingPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real Api in the Test environment with explicit dependency connection strings.
/// Test-only controllers in this assembly are added as an application part. Each factory gets a random signing key
/// and its own rate-limit namespace so tests never share counters.
/// </summary>
public sealed class ApiFactory(
    string postgresConnectionString,
    string redisConnectionString,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    // Ports that refuse connections immediately, for tests that need dependencies to be down.
    public const string UnreachablePostgres = "Host=127.0.0.1;Port=1;Database=unreachable;Username=unreachable;Password=unreachable;Timeout=2";
    public const string UnreachableRedis = "127.0.0.1:1,abortConnect=false,connectTimeout=1000";
    public const string AllowedOrigin = "http://localhost:9000";

    public static ApiFactory WithUnreachableDependencies() => new(UnreachablePostgres, UnreachableRedis);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = postgresConnectionString,
            ["ConnectionStrings:Redis"] = redisConnectionString,
            ["Cors:AllowedOrigins:0"] = AllowedOrigin,
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["PublicBooking:TokenKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["RateLimiting:KeyPrefix"] = "test-" + Guid.NewGuid().ToString("N"),
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            configuration[key] = value;
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));

        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly));
    }

    /// <summary>HTTPS base address so Secure cookies behave like production; cookies are managed explicitly by tests.</summary>
    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
        AllowAutoRedirect = false,
    });
}

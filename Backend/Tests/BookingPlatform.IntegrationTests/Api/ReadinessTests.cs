using System.Net;
using System.Text.Json;
using BookingPlatform.IntegrationTests.Infrastructure;

namespace BookingPlatform.IntegrationTests.Api;

[Collection(InfrastructureCollection.Name)]
[Trait("Category", "Docker")]
public sealed class ReadinessTests(InfrastructureFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Readiness_is_healthy_when_postgres_and_redis_are_reachable()
    {
        var postgres = await fixture.CreateMigratedDatabaseAsync();
        await using var factory = new ApiFactory(postgres, fixture.RedisConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        body.RootElement.GetProperty("checks").EnumerateArray()
            .Select(check => (check.GetProperty("name").GetString(), check.GetProperty("status").GetString()))
            .Should().BeEquivalentTo([("postgres", "Healthy"), ("redis", "Healthy")]);
    }

    [Fact]
    public async Task Readiness_reports_only_the_failing_dependency()
    {
        var postgres = await fixture.CreateMigratedDatabaseAsync();
        await using var factory = new ApiFactory(postgres, ApiFactory.UnreachableRedis);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("checks").EnumerateArray()
            .Select(check => (check.GetProperty("name").GetString(), check.GetProperty("status").GetString()))
            .Should().BeEquivalentTo([("postgres", "Healthy"), ("redis", "Unhealthy")]);
    }
}

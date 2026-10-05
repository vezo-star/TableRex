using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingPlatform.IntegrationTests.Infrastructure;

namespace BookingPlatform.IntegrationTests.Api;

public sealed class UnreachableDependenciesApiFixture : IDisposable
{
    public ApiFactory Factory { get; } = ApiFactory.WithUnreachableDependencies();

    public void Dispose() => Factory.Dispose();
}

/// <summary>
/// Cross-cutting API behaviour that does not need real dependencies: health semantics, error model, correlation, headers.
/// </summary>
public sealed class ApiPipelineTests(UnreachableDependenciesApiFixture fixture) : IClassFixture<UnreachableDependenciesApiFixture>
{
    private readonly HttpClient _client = fixture.Factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Liveness_is_healthy_without_running_dependency_checks()
    {
        var response = await _client.GetAsync("/health", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        body.RootElement.GetProperty("checks").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Readiness_is_unavailable_when_dependencies_are_unreachable_and_leaks_no_details()
    {
        var response = await _client.GetAsync("/health/ready", Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var body = JsonDocument.Parse(raw);
        body.RootElement.GetProperty("status").GetString().Should().Be("Unhealthy");

        var checks = body.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(check => check.GetProperty("name").GetString()!, check => check.GetProperty("status").GetString());
        checks.Should().BeEquivalentTo(new Dictionary<string, string?> { ["postgres"] = "Unhealthy", ["redis"] = "Unhealthy" });

        raw.Should().NotContainAny("127.0.0.1", "Password", "Exception", "unreachable (");
    }

    [Fact]
    public async Task Valid_correlation_id_is_echoed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", "client-abc_123.x");

        var response = await _client.SendAsync(request, Ct);

        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle().Which.Should().Be("client-abc_123.x");
    }

    [Fact]
    public async Task Invalid_correlation_id_is_replaced_with_a_generated_one()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "not valid <script>");

        var response = await _client.SendAsync(request, Ct);

        var echoed = response.Headers.GetValues("X-Correlation-Id").Single();
        echoed.Should().NotBe("not valid <script>").And.MatchRegex("^[a-f0-9]{32}$");
    }

    [Fact]
    public async Task Security_headers_are_set()
    {
        var response = await _client.GetAsync("/health", Ct);

        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle("no-referrer");
    }

    [Fact]
    public async Task Unhandled_exception_returns_generic_problem_without_internal_details()
    {
        var response = await _client.GetAsync("/__tests/errors/unhandled", Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        raw.Should().NotContainAny("hunter2", "db.internal", "InvalidOperationException", " at ");

        using var body = JsonDocument.Parse(raw);
        body.RootElement.GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        body.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("/__tests/errors/not-found", HttpStatusCode.NotFound, "VENUE_NOT_FOUND")]
    [InlineData("/__tests/errors/conflict", HttpStatusCode.Conflict, "RESOURCE_NOT_AVAILABLE")]
    [InlineData("/__tests/errors/forbidden", HttpStatusCode.Forbidden, "FORBIDDEN")]
    [InlineData("/api/v1/does-not-exist", HttpStatusCode.NotFound, "NOT_FOUND")]
    public async Task Expected_errors_map_to_status_and_stable_code(string path, HttpStatusCode status, string code)
    {
        var response = await _client.GetAsync(path, Ct);

        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
        body.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
        body.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Application_validation_errors_return_400_with_field_errors()
    {
        var response = await _client.GetAsync("/__tests/errors/validation", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        body.RootElement.GetProperty("errors").GetProperty("partySize").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Model_binding_validation_errors_use_the_same_error_model()
    {
        var response = await _client.PostAsJsonAsync("/__tests/errors/model", new { partySize = 0 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        body.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("errors").EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Swagger_document_is_served_in_the_test_environment()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("info").GetProperty("title").GetString().Should().Be("Booking Platform API");
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
}

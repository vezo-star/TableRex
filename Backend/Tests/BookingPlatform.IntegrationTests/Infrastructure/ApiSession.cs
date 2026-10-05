using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BookingPlatform.IntegrationTests.Infrastructure;

/// <summary>A signed-in API client: holds the access token and the refresh cookie explicitly (no cookie container).</summary>
public sealed class ApiSession(HttpClient client)
{
    public const string RefreshCookieName = "bp_refresh";

    public HttpClient Client { get; } = client;

    public string? AccessToken { get; private set; }

    public string? RefreshToken { get; set; }

    public JsonElement LastAuthBody { get; private set; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task<HttpResponseMessage> LoginAsync(string email, string password)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, Ct);
        await CaptureAsync(response);
        return response;
    }

    public async Task<ApiSession> LoginOkAsync(string email, string password)
    {
        var response = await LoginAsync(email, password);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return this;
    }

    public async Task<HttpResponseMessage> RefreshAsync(string? refreshToken = null, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        var token = refreshToken ?? RefreshToken;
        if (token is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookieName}={token}");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        var response = await Client.SendAsync(request, Ct);
        await CaptureAsync(response);
        return response;
    }

    public async Task<HttpResponseMessage> PostWithCookieAsync(string path, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (RefreshToken is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookieName}={RefreshToken}");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        Authorize(request);
        var response = await Client.SendAsync(request, Ct);
        await CaptureAsync(response);
        return response;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        Authorize(request);
        return await Client.SendAsync(request, Ct);
    }

    public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(HttpMethod.Get, path);

    public void UseAccessToken(string token) => AccessToken = token;

    private void Authorize(HttpRequestMessage request)
    {
        if (AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }
    }

    private async Task CaptureAsync(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies.Where(c => c.StartsWith(RefreshCookieName + "=", StringComparison.Ordinal)))
            {
                var value = cookie[(RefreshCookieName.Length + 1)..].Split(';')[0];
                RefreshToken = string.IsNullOrEmpty(value) ? null : value;
            }
        }

        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            LastAuthBody = body;
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("accessToken", out var token))
            {
                AccessToken = token.GetString();
            }
        }
    }
}

public static class HttpResponseExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    public static async Task ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(status, raw);
        using var body = JsonDocument.Parse(raw);
        body.RootElement.GetProperty("code").GetString().Should().Be(code, raw);
    }
}

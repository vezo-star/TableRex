using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace BookingPlatform.Api.Configuration;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];

    public static bool AreValid(CorsSettings settings) =>
        settings.AllowedOrigins.All(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && uri.AbsolutePath == "/"
            && !origin.EndsWith('/'));
}

/// <summary>
/// Builds the default CORS policy from validated settings. Only explicitly listed origins are allowed; never a wildcard.
/// </summary>
public sealed class ConfigureCorsPolicy(IOptions<CorsSettings> settings) : IConfigureOptions<CorsOptions>
{
    public void Configure(CorsOptions options)
    {
        options.AddDefaultPolicy(policy => policy
            .WithOrigins(settings.Value.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders(Middleware.CorrelationIdMiddleware.HeaderName, Controllers.IdempotencyExtensions.ReplayedHeader, "Retry-After"));
    }
}

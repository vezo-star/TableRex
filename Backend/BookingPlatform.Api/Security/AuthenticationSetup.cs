using BookingPlatform.Api.Errors;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BookingPlatform.Api.Security;

public static class AuthenticationSetup
{
    public static IServiceCollection AddPlatformAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IRequestMetadata, HttpRequestMetadata>();
        services.AddScoped<RequestTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<RequestTenantContext>());

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.RequireHttpsMetadata = false;
                bearer.SaveToken = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(jwt.GetKeyBytes()),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = PlatformClaims.Subject,
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // SignalR browsers cannot set headers on WebSockets; accept access_token only on hub paths.
                        var token = context.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        var problem = new ProblemDetails
                        {
                            Status = StatusCodes.Status401Unauthorized,
                            Title = "Unauthorized",
                            Detail = "Authentication is required.",
                        };
                        problem.Extensions[ProblemDetailsDefaults.CodeKey] = ErrorCodes.Unauthorized;
                        var writer = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                        await writer.WriteAsync(new ProblemDetailsContext { HttpContext = context.HttpContext, ProblemDetails = problem });
                    },
                };
            });

        // Secure by default: an endpoint (or unmatched path) without authorization metadata needs a signed-in user.
        // Anonymous endpoints opt out with [AllowAnonymous]; tenant data additionally needs a permission attribute.
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();

        return services;
    }

    /// <summary>
    /// Trusts X-Forwarded-For/Proto only from configured proxy networks (ReverseProxy:TrustedNetworks, CIDR).
    /// Without configuration only loopback proxies are trusted, so clients cannot spoof their IP for rate limits.
    /// </summary>
    public static IServiceCollection AddPlatformForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = configuration.GetSection("ReverseProxy:TrustedNetworks").Get<string[]>() ?? [];
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 2;
            foreach (var network in networks)
            {
                if (!System.Net.IPNetwork.TryParse(network, out var parsed))
                {
                    throw new InvalidOperationException($"ReverseProxy:TrustedNetworks contains an invalid CIDR '{network}'.");
                }

                options.KnownIPNetworks.Add(parsed);
            }
        });
        return services;
    }
}

/// <summary>Refresh cookie: HttpOnly, Secure, SameSite=Strict, scoped to the auth path (SECURITY.md §1).</summary>
public static class RefreshCookie
{
    public const string Name = "bp_refresh";
    public const string Path = "/api/v1/auth";

    public static void Set(HttpResponse response, string token, DateTime expiresAt) =>
        response.Cookies.Append(Name, token, Options(expiresAt));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(null));

    public static string? Read(HttpRequest request) => request.Cookies.TryGetValue(Name, out var value) ? value : null;

    private static CookieOptions Options(DateTime? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        IsEssential = true,
        Expires = expiresAt is null ? null : new DateTimeOffset(expiresAt.Value, TimeSpan.Zero),
    };
}

/// <summary>CSRF defence for cookie-authenticated endpoints: a present Origin must be same-origin or allow-listed.</summary>
public static class OriginCheck
{
    public static bool IsAllowed(HttpRequest request, IReadOnlyCollection<string> allowedOrigins)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            // Browsers always send Origin on cross-site POSTs; its absence means same-origin or a non-browser client.
            return true;
        }

        if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        // Same-origin (scheme may differ when TLS terminates at the proxy).
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}

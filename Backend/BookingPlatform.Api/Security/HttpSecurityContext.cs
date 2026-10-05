using BookingPlatform.Api.Middleware;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Infrastructure.Security;

namespace BookingPlatform.Api.Security;

/// <summary>Reads the validated access token claims of the current request.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string PlatformAdminItemKey = "BookingPlatform.IsPlatformAdmin";

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true && UserId is not null;

    public Guid? UserId => ParseClaim(PlatformClaims.Subject);

    public Guid? TokenTenantId => ParseClaim(PlatformClaims.TenantId);

    public bool IsPlatformAdmin => accessor.HttpContext?.Items[PlatformAdminItemKey] is true;

    private Guid? ParseClaim(string type)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return Guid.TryParse(principal.FindFirst(type)?.Value, out var value) ? value : null;
    }
}

public sealed class HttpRequestMetadata(IHttpContextAccessor accessor) : IRequestMetadata
{
    private const int MaxUserAgentLength = 512;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var sanitized = new string(value.Where(c => !char.IsControl(c)).ToArray());
            return sanitized.Length <= MaxUserAgentLength ? sanitized : sanitized[..MaxUserAgentLength];
        }
    }

    public string? CorrelationId => accessor.HttpContext is { } context ? CorrelationIdMiddleware.Get(context) : null;
}

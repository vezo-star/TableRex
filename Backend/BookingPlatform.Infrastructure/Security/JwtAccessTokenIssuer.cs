using System.Security.Claims;
using BookingPlatform.Application.Common.Time;
using BookingPlatform.Application.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BookingPlatform.Infrastructure.Security;

public static class PlatformClaims
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string TenantId = "tid";
}

/// <summary>Issues short-lived access tokens. No permissions are embedded; they are resolved per request.</summary>
public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, IClock clock) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public AccessToken Issue(Guid userId, Guid? tenantId)
    {
        var settings = options.Value;
        var now = clock.UtcNow;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = userId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
        };
        if (tenantId is { } tid)
        {
            claims[PlatformClaims.TenantId] = tid.ToString();
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Claims = claims,
            Subject = new ClaimsIdentity(),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(settings.GetKeyBytes()), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expires);
    }
}

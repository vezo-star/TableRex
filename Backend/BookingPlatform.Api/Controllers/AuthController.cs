using System.ComponentModel.DataAnnotations;
using BookingPlatform.Api.Configuration;
using BookingPlatform.Api.Security;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Identity;
using BookingPlatform.Application.Venues;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BookingPlatform.Api.Controllers;

public sealed record LoginRequest(
    [Required, MaxLength(254)] string Email,
    [Required, MaxLength(PasswordPolicy.MaxLength * 4)] string Password);

public sealed record SwitchTenantRequest([Required] Guid TenantId);

public sealed record ChangePasswordRequest(
    [Required, MaxLength(PasswordPolicy.MaxLength * 4)] string CurrentPassword,
    [Required, MaxLength(PasswordPolicy.MaxLength * 4)] string NewPassword);

public sealed record ForgotPasswordRequest([Required, EmailAddress, MaxLength(254)] string Email);

public sealed record ResetPasswordRequest(
    [Required, MaxLength(128)] string Token,
    [Required, MaxLength(PasswordPolicy.MaxLength * 4)] string NewPassword);

public sealed record MembershipResponse(Guid TenantId, string TenantName, string TenantSlug, IReadOnlyList<string> Roles);

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    Guid UserId,
    Guid? TenantId,
    bool RequiresTenantSelection,
    IReadOnlyList<MembershipResponse> Memberships);

public sealed record MeResponse(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsPlatformAdmin,
    MembershipResponse? CurrentTenant,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<MembershipResponse> Memberships,
    IReadOnlyList<Guid> AccessibleVenueIds,
    bool HasAllVenueAccess);

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(AuthService auth, IOptions<CorsSettings> cors) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [RateLimit("login", 20, 60)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var session = await auth.LoginAsync(request.Email, request.Password, cancellationToken);
        return Issue(session);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [RateLimit("refresh", 60, 60)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        EnsureAllowedOrigin();
        try
        {
            var session = await auth.RefreshAsync(RefreshCookie.Read(Request), cancellationToken);
            return Issue(session);
        }
        catch (UnauthorizedAppException)
        {
            RefreshCookie.Clear(Response);
            throw;
        }
    }

    /// <summary>Revokes the refresh token family of the cookie. Works with an expired access token.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        EnsureAllowedOrigin();
        await auth.LogoutAsync(RefreshCookie.Read(Request), cancellationToken);
        RefreshCookie.Clear(Response);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Me(
        [FromServices] ICurrentUser currentUser, [FromServices] ITenantContext tenantContext,
        [FromServices] IVenueAccessRepository venueAccess, CancellationToken cancellationToken)
    {
        var me = await auth.GetMeAsync(currentUser.RequireUserId(), tenantContext, cancellationToken);
        IReadOnlyList<Guid> venueIds = !tenantContext.HasTenant ? []
            : tenantContext.HasAllVenueAccess ? await venueAccess.ListAllVenueIdsAsync(tenantContext.TenantId, cancellationToken)
            : await venueAccess.ListAssignedAsync(tenantContext.TenantId, tenantContext.TenantUserId, cancellationToken);
        return new MeResponse(
            me.UserId,
            me.Email,
            me.DisplayName,
            me.IsPlatformAdmin,
            me.CurrentTenant is null ? null : Map(me.CurrentTenant),
            me.Roles,
            me.Permissions,
            me.Memberships.Select(Map).ToList(),
            venueIds,
            tenantContext.HasAllVenueAccess);
    }

    [HttpPost("switch-tenant")]
    [Authorize]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuthResponse>> SwitchTenant(
        SwitchTenantRequest request, [FromServices] ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        EnsureAllowedOrigin();
        var session = await auth.SwitchTenantAsync(currentUser.RequireUserId(), request.TenantId, RefreshCookie.Read(Request), cancellationToken);
        return Issue(session);
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> ChangePassword(
        ChangePasswordRequest request, [FromServices] ICurrentUser currentUser, [FromServices] ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        EnsureAllowedOrigin();
        var session = await auth.ChangePasswordAsync(
            currentUser.RequireUserId(), tenantContext.HasTenant ? tenantContext.TenantId : null,
            request.CurrentPassword, request.NewPassword, cancellationToken);
        return Issue(session);
    }

    /// <summary>Emails a single-use reset link when the address belongs to an active account. Always 202.</summary>
    [HttpPost("password/forgot")]
    [AllowAnonymous]
    [RateLimit("password-forgot", 5, 900)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.RequestPasswordResetAsync(request.Email, cancellationToken);
        return Accepted();
    }

    /// <summary>Sets a new password from an emailed token and signs out every session of the account.</summary>
    [HttpPost("password/reset")]
    [AllowAnonymous]
    [RateLimit("password-reset", 10, 900)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    private AuthResponse Issue(AuthSession session)
    {
        RefreshCookie.Set(Response, session.RefreshToken.Token, session.RefreshToken.ExpiresAt);
        Response.Headers.CacheControl = "no-store";
        return new AuthResponse(
            session.AccessToken.Token,
            session.AccessToken.ExpiresAt,
            session.UserId,
            session.TenantId,
            session.TenantId is null && session.Memberships.Count > 1,
            session.Memberships.Select(Map).ToList());
    }

    private void EnsureAllowedOrigin()
    {
        if (!OriginCheck.IsAllowed(Request, cors.Value.AllowedOrigins))
        {
            throw new ForbiddenAppException("Cross-origin request rejected.");
        }
    }

    private static MembershipResponse Map(MembershipSummary m) => new(m.TenantId, m.TenantName, m.TenantSlug, m.Roles);
}

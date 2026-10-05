using BookingPlatform.Api.Errors;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Tenancy;
using BookingPlatform.Domain.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Security;

/// <summary>
/// For authenticated requests: confirms the user is still active and, when the token carries <c>tid</c>, that the
/// membership is still active (cached ≤60 s). Populates <see cref="RequestTenantContext"/>; never trusts the claim alone.
/// </summary>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context, ICurrentUser currentUser, RequestTenantContext tenantContext, ITenantAccessCache accessCache,
        IProblemDetailsService problemDetails)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        if (currentUser.UserId is not { } userId)
        {
            await WriteProblemAsync(context, problemDetails, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
            return;
        }

        var flags = await accessCache.GetUserAsync(userId, context.RequestAborted);
        if (flags is not { Status: UserStatus.Active })
        {
            await WriteProblemAsync(context, problemDetails, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
            return;
        }

        context.Items[HttpCurrentUser.PlatformAdminItemKey] = flags.IsPlatformAdmin;

        if (currentUser.TokenTenantId is { } tenantId)
        {
            var access = await accessCache.GetAsync(userId, tenantId, context.RequestAborted);
            if (access is null)
            {
                await WriteProblemAsync(context, problemDetails, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                    "You no longer have access to this tenant.");
                return;
            }

            tenantContext.Set(access);
        }

        await next(context);
    }

    private static async Task WriteProblemAsync(HttpContext context, IProblemDetailsService problemDetails, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status == StatusCodes.Status401Unauthorized ? "Unauthorized" : "Forbidden",
            Detail = detail,
        };
        problem.Extensions[ProblemDetailsDefaults.CodeKey] = code;
        await problemDetails.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}

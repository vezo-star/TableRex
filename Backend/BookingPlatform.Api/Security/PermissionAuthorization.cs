using BookingPlatform.Api.Errors;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BookingPlatform.Api.Security;

/// <summary>Requires a verified tenant context whose roles grant <paramref name="permission"/>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicyProvider.Prefix + permission)
{
    public string Permission { get; } = permission;
}

/// <summary>Requires an authenticated user with a verified tenant context (any role).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireTenantAttribute() : AuthorizeAttribute(PermissionPolicyProvider.TenantMemberPolicy);

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequirePlatformAdminAttribute() : AuthorizeAttribute(PermissionPolicyProvider.PlatformAdminPolicy);

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class TenantMemberRequirement : IAuthorizationRequirement;

public sealed class PlatformAdminRequirement : IAuthorizationRequirement;

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    public const string Prefix = "perm:";
    public const string TenantMemberPolicy = "tenant-member";
    public const string PlatformAdminPolicy = "platform-admin";

    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        AuthorizationPolicy? policy = policyName switch
        {
            TenantMemberPolicy => Build(new TenantMemberRequirement()),
            PlatformAdminPolicy => Build(new PlatformAdminRequirement()),
            _ when policyName.StartsWith(Prefix, StringComparison.Ordinal) => Build(new PermissionRequirement(policyName[Prefix.Length..])),
            _ => null,
        };

        return policy is not null ? Task.FromResult<AuthorizationPolicy?>(policy) : _fallback.GetPolicyAsync(policyName);
    }

    private static AuthorizationPolicy Build(IAuthorizationRequirement requirement) =>
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(requirement).Build();
}

public sealed class PermissionAuthorizationHandler(ITenantContext tenantContext, ICurrentUser currentUser)
    : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements.ToList())
        {
            var satisfied = requirement switch
            {
                PermissionRequirement permission => tenantContext.HasPermission(permission.Permission),
                TenantMemberRequirement => tenantContext.HasTenant,
                PlatformAdminRequirement => currentUser.IsPlatformAdmin,
                _ => (bool?)null,
            };

            if (satisfied == true)
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>Writes 401/403 as ProblemDetails with stable codes instead of empty bodies.</summary>
public sealed class ProblemDetailsAuthorizationResultHandler(IProblemDetailsService problemDetails)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        if (authorizeResult.Challenged)
        {
            await WriteAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized", ErrorCodes.Unauthorized, "Authentication is required.");
            return;
        }

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        var needsTenant = !tenantContext.HasTenant
            && policy.Requirements.Any(r => r is PermissionRequirement or TenantMemberRequirement);
        await WriteAsync(context, StatusCodes.Status403Forbidden, "Forbidden",
            needsTenant ? ErrorCodes.TenantSelectionRequired : ErrorCodes.Forbidden,
            needsTenant ? "Select a tenant before accessing tenant data." : "You do not have permission to perform this action.");
    }

    private async Task WriteAsync(HttpContext context, int status, string title, string code, string detail)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions[ProblemDetailsDefaults.CodeKey] = code;
        await problemDetails.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}

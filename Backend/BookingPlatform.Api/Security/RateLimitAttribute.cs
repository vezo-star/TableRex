using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BookingPlatform.Api.Security;

/// <summary>
/// Per-client-IP fixed-window limit backed by <see cref="IRateLimiter"/> (Redis, in-memory fallback). The defaults can be
/// overridden per policy with <c>RateLimiting:Policies:{policy}:PermitLimit</c> and <c>…:WindowSeconds</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RateLimitAttribute(string policy, int permitLimit, int windowSeconds) : Attribute, IFilterFactory
{
    public bool IsReusable => true;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
    {
        var section = serviceProvider.GetRequiredService<IConfiguration>().GetSection($"RateLimiting:Policies:{policy}");
        var limit = section.GetValue<int?>("PermitLimit") is { } configuredLimit && configuredLimit > 0 ? configuredLimit : permitLimit;
        var window = section.GetValue<int?>("WindowSeconds") is { } configuredWindow && configuredWindow > 0 ? configuredWindow : windowSeconds;
        return new RateLimitFilter(serviceProvider.GetRequiredService<IRateLimiter>(), policy, limit, TimeSpan.FromSeconds(window));
    }

    private sealed class RateLimitFilter(IRateLimiter limiter, string policy, int permitLimit, TimeSpan window) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var decision = await limiter.AcquireAsync($"{policy}:ip:{ip}", permitLimit, window, context.HttpContext.RequestAborted);
            if (!decision.Allowed)
            {
                throw new RateLimitedAppException(decision.RetryAfterSeconds);
            }

            await next();
        }
    }
}

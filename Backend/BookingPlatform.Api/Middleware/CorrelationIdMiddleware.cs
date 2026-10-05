using System.Text.RegularExpressions;
using Serilog.Context;

namespace BookingPlatform.Api.Middleware;

/// <summary>
/// Accepts a well-formed client <c>X-Correlation-Id</c> or generates one, exposes it to logs and echoes it on the response.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedFormat();

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName].ToString());
        context.Items[ItemKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemKey, correlationId))
        {
            await next(context);
        }
    }

    public static string? Get(HttpContext context) => context.Items[ItemKey] as string;

    private static string ResolveCorrelationId(string candidate) =>
        AllowedFormat().IsMatch(candidate) ? candidate : Guid.CreateVersion7().ToString("N");
}

using System.Globalization;
using BookingPlatform.Application.Common.Idempotency;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Controllers;

public static class IdempotencyExtensions
{
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>Reads the Idempotency-Key header and hashes method + route + the canonical (re-serialised) body.</summary>
    public static IdempotencyRequest CreateIdempotencyRequest<T>(this ControllerBase controller, string route, T body)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var serializer = controller.HttpContext.RequestServices.GetRequiredService<IResponseSerializer>();
        var key = controller.Request.Headers[KeyHeader].ToString();
        return IdempotencyRequest.Create(key, controller.Request.Method, route, serializer.Serialize(body));
    }

    /// <summary>201 with the fresh result, or the stored response with <c>Idempotent-Replayed: true</c>.</summary>
    public static IActionResult IdempotentCreated<T>(this ControllerBase controller, IdempotentResult<T> result, Func<T, string> location)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Replay is { } replay)
        {
            controller.Response.Headers[ReplayedHeader] = "true";
            return new ContentResult { StatusCode = replay.Status, Content = replay.Body, ContentType = "application/json; charset=utf-8" };
        }

        return controller.Created(location(result.Value!), result.Value);
    }

    /// <summary>The version from the body, else from an <c>If-Match</c> header (<c>"3"</c> or <c>3</c>).</summary>
    public static int? ResolveVersion(this ControllerBase controller, int? bodyVersion)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (bodyVersion is not null)
        {
            return bodyVersion;
        }

        var header = controller.Request.Headers.IfMatch.ToString().Trim().Trim('"');
        if (header.StartsWith("W/", StringComparison.Ordinal))
        {
            header = header[2..].Trim('"');
        }

        return int.TryParse(header, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;
    }
}

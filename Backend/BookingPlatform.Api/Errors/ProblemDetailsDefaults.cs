using System.Diagnostics;
using BookingPlatform.Api.Middleware;
using BookingPlatform.Application.Common.Errors;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Errors;

public static class ProblemDetailsDefaults
{
    public const string CodeKey = "code";
    public const string TraceIdKey = "traceId";
    public const string CorrelationIdKey = "correlationId";

    /// <summary>
    /// Applied to every ProblemDetails the framework writes (exceptions, status-code pages, model validation)
    /// so all errors carry a stable <c>code</c> and a <c>traceId</c>, and never an exception dump.
    /// </summary>
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var httpContext = context.HttpContext;

        problem.Extensions.Remove("exception");

        problem.Extensions[TraceIdKey] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        var correlationId = CorrelationIdMiddleware.Get(httpContext);
        if (correlationId is not null)
        {
            problem.Extensions[CorrelationIdKey] = correlationId;
        }

        if (!problem.Extensions.ContainsKey(CodeKey))
        {
            problem.Extensions[CodeKey] = DefaultCode(problem);
        }

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            problem.Detail = "An unexpected error occurred.";
        }
    }

    private static string DefaultCode(ProblemDetails problem) => problem switch
    {
        HttpValidationProblemDetails => ErrorCodes.ValidationError,
        { Status: StatusCodes.Status400BadRequest } => ErrorCodes.BadRequest,
        { Status: StatusCodes.Status401Unauthorized } => ErrorCodes.Unauthorized,
        { Status: StatusCodes.Status403Forbidden } => ErrorCodes.Forbidden,
        { Status: StatusCodes.Status404NotFound } => ErrorCodes.NotFound,
        { Status: StatusCodes.Status405MethodNotAllowed } => ErrorCodes.MethodNotAllowed,
        { Status: StatusCodes.Status409Conflict } => ErrorCodes.Conflict,
        { Status: StatusCodes.Status429TooManyRequests } => ErrorCodes.RateLimited,
        _ => ErrorCodes.InternalError,
    };
}

using System.Globalization;
using BookingPlatform.Application.Common.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Errors;

/// <summary>
/// Converts exceptions into RFC 7807 responses. Expected <see cref="AppException"/>s keep their safe message;
/// anything else becomes a generic 500 so internal details never reach clients.
/// </summary>
public sealed class AppExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<AppExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; there is nobody to answer.
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var problem = exception switch
        {
            ValidationAppException validation => new HttpValidationProblemDetails(validation.Errors.ToDictionary())
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Detail = validation.Message,
            },
            BadRequestAppException e => Create(StatusCodes.Status400BadRequest, "Bad request", e.Message),
            UnauthorizedAppException e => Create(StatusCodes.Status401Unauthorized, "Unauthorized", e.Message),
            ForbiddenAppException e => Create(StatusCodes.Status403Forbidden, "Forbidden", e.Message),
            NotFoundAppException e => Create(StatusCodes.Status404NotFound, "Not found", e.Message),
            ConflictAppException e => Create(StatusCodes.Status409Conflict, "Conflict", e.Message),
            GoneAppException e => Create(StatusCodes.Status410Gone, "Gone", e.Message),
            BusinessRuleAppException e => Create(StatusCodes.Status422UnprocessableEntity, "Business rule violated", e.Message),
            LockedAppException e => Create(StatusCodes.Status423Locked, "Locked", e.Message),
            RateLimitedAppException e => Create(StatusCodes.Status429TooManyRequests, "Too many requests", e.Message),
            BadHttpRequestException badRequest => Create(badRequest.StatusCode, "Bad request", "The request could not be processed."),
            _ => Create(StatusCodes.Status500InternalServerError, "Internal server error", "An unexpected error occurred."),
        };

        problem.Extensions[ProblemDetailsDefaults.CodeKey] = exception switch
        {
            AppException appException => appException.Code,
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => ErrorCodes.PayloadTooLarge,
            BadHttpRequestException => ErrorCodes.BadRequest,
            _ => ErrorCodes.InternalError,
        };

        if (exception is AppException { Extensions.Count: > 0 } withExtensions)
        {
            foreach (var (key, value) in withExtensions.Extensions)
            {
                problem.Extensions.TryAdd(key, value);
            }
        }

        if (exception is RateLimitedAppException rateLimited)
        {
            httpContext.Response.Headers.RetryAfter = rateLimited.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Create(int status, string title, string detail) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
    };
}

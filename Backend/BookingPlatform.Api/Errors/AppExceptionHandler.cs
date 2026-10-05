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
        var problem = exception switch
        {
            ValidationAppException validation => new HttpValidationProblemDetails(validation.Errors.ToDictionary())
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Detail = validation.Message,
            },
            NotFoundAppException notFound => Create(StatusCodes.Status404NotFound, "Not found", notFound.Message),
            ConflictAppException conflict => Create(StatusCodes.Status409Conflict, "Conflict", conflict.Message),
            ForbiddenAppException forbidden => Create(StatusCodes.Status403Forbidden, "Forbidden", forbidden.Message),
            BadHttpRequestException badRequest => Create(badRequest.StatusCode, "Bad request", "The request could not be processed."),
            _ => Create(StatusCodes.Status500InternalServerError, "Internal server error", "An unexpected error occurred."),
        };

        problem.Extensions[ProblemDetailsDefaults.CodeKey] = exception switch
        {
            AppException appException => appException.Code,
            BadHttpRequestException => ErrorCodes.BadRequest,
            _ => ErrorCodes.InternalError,
        };

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

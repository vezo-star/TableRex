namespace BookingPlatform.Application.Common.Errors;

/// <summary>
/// Stable application error codes returned in ProblemDetails <c>code</c>. Clients depend on these values; never rename one.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string BadRequest = "BAD_REQUEST";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string Conflict = "CONFLICT";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
}

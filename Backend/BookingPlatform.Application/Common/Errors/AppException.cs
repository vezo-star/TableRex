namespace BookingPlatform.Application.Common.Errors;

/// <summary>
/// Base type for expected, client-facing failures. The API maps each subtype to an HTTP status;
/// the message is safe to show to callers, so it must never contain internal details.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }

    /// <summary>Additional safe ProblemDetails members (for example conflicting resource ids on the staff API).</summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; init; } = new Dictionary<string, object?>();
}

public sealed class ValidationAppException : AppException
{
    public ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
        : base(ErrorCodes.ValidationError, "One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationAppException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>400 for request-level problems that are not field validation (for example a missing header).</summary>
public sealed class BadRequestAppException(string code, string message) : AppException(code, message);

public sealed class UnauthorizedAppException(string code, string message) : AppException(code, message);

public sealed class NotFoundAppException(string code, string message) : AppException(code, message);

public sealed class ConflictAppException(string code, string message) : AppException(code, message);

public sealed class ForbiddenAppException(string message = "You do not have permission to perform this action.")
    : AppException(ErrorCodes.Forbidden, message);

/// <summary>422: the request is well formed but breaks a business rule (operating hours, party size, ...).</summary>
public sealed class BusinessRuleAppException(string code, string message) : AppException(code, message);

public sealed class GoneAppException(string code, string message) : AppException(code, message);

public sealed class LockedAppException(string code, string message) : AppException(code, message);

public sealed class RateLimitedAppException(int retryAfterSeconds)
    : AppException(ErrorCodes.RateLimited, "Too many requests. Please wait and try again.")
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

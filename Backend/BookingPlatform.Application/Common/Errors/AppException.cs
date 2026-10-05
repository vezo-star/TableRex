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
}

public sealed class ValidationAppException : AppException
{
    public ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
        : base(ErrorCodes.ValidationError, "One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class NotFoundAppException : AppException
{
    public NotFoundAppException(string code, string message)
        : base(code, message)
    {
    }
}

public sealed class ConflictAppException : AppException
{
    public ConflictAppException(string code, string message)
        : base(code, message)
    {
    }
}

public sealed class ForbiddenAppException : AppException
{
    public ForbiddenAppException(string message = "You do not have permission to perform this action.")
        : base(ErrorCodes.Forbidden, message)
    {
    }
}

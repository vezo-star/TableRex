using BookingPlatform.Application.Common.Security;

namespace BookingPlatform.Infrastructure.Security;

/// <summary>No signed-in user: the identity seen by services running outside an HTTP request (the Worker).</summary>
public sealed class BackgroundCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public Guid? TokenTenantId => null;

    public bool IsPlatformAdmin => false;
}

/// <summary>Request metadata outside an HTTP request: nothing to record.</summary>
public sealed class BackgroundRequestMetadata : IRequestMetadata
{
    public string? IpAddress => null;

    public string? UserAgent => null;

    public string? CorrelationId => null;
}

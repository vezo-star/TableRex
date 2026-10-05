namespace BookingPlatform.Application.Common.RateLimiting;

public readonly record struct RateLimitDecision(bool Allowed, int RetryAfterSeconds);

/// <summary>Fixed-window counters shared across instances (Redis), falling back to per-instance memory when Redis is down.</summary>
public interface IRateLimiter
{
    Task<RateLimitDecision> AcquireAsync(string key, int permitLimit, TimeSpan window, CancellationToken cancellationToken);
}

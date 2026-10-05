using System.Collections.Concurrent;
using BookingPlatform.Application.Common.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace BookingPlatform.Infrastructure.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Namespace for counters so environments sharing a Redis instance do not collide.</summary>
    public string KeyPrefix { get; set; } = "rl";
}

/// <summary>
/// Fixed-window counters in Redis (shared across instances). If Redis is unavailable the limiter degrades to a
/// per-instance in-memory window rather than failing requests — Redis is never authoritative (D-005).
/// </summary>
public sealed class RedisRateLimiter(
    IConnectionMultiplexer redis, IOptions<RateLimitingOptions> options, ILogger<RedisRateLimiter> logger) : IRateLimiter
{
    private const string Script = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
            ttl = tonumber(ARGV[1])
        end
        return { current, ttl }
        """;

    private readonly ConcurrentDictionary<string, Window> _fallback = new(StringComparer.Ordinal);
    private long _lastFallbackWarningTicks;

    public async Task<RateLimitDecision> AcquireAsync(string key, int permitLimit, TimeSpan window, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(permitLimit, 1);

        var redisKey = $"{options.Value.KeyPrefix}:{key}";
        if (redis.IsConnected)
        {
            try
            {
                var result = (RedisResult[]?)await redis.GetDatabase()
                    .ScriptEvaluateAsync(Script, [redisKey], [(long)window.TotalMilliseconds]);
                if (result is { Length: 2 })
                {
                    var count = (long)result[0];
                    var ttlMs = Math.Max(0, (long)result[1]);
                    return count <= permitLimit
                        ? new RateLimitDecision(true, 0)
                        : new RateLimitDecision(false, (int)Math.Max(1, Math.Ceiling(ttlMs / 1000d)));
                }
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException or ObjectDisposedException)
            {
                WarnFallback(ex);
            }
        }
        else
        {
            WarnFallback(null);
        }

        return AcquireInMemory(redisKey, permitLimit, window);
    }

    private RateLimitDecision AcquireInMemory(string key, int permitLimit, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        if (_fallback.Count > 50_000)
        {
            foreach (var expired in _fallback.Where(pair => pair.Value.ResetsAt <= now).Select(pair => pair.Key).ToList())
            {
                _fallback.TryRemove(expired, out _);
            }
        }

        var entry = _fallback.AddOrUpdate(
            key,
            _ => new Window(1, now + window),
            (_, existing) => existing.ResetsAt <= now ? new Window(1, now + window) : existing with { Count = existing.Count + 1 });

        return entry.Count <= permitLimit
            ? new RateLimitDecision(true, 0)
            : new RateLimitDecision(false, (int)Math.Max(1, Math.Ceiling((entry.ResetsAt - now).TotalSeconds)));
    }

    private void WarnFallback(Exception? exception)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastFallbackWarningTicks);
        if (now - last < TimeSpan.TicksPerMinute || Interlocked.CompareExchange(ref _lastFallbackWarningTicks, now, last) != last)
        {
            return;
        }

        logger.LogWarning(exception, "Redis unavailable for rate limiting; using per-instance in-memory limits");
    }

    private sealed record Window(int Count, DateTime ResetsAt);
}

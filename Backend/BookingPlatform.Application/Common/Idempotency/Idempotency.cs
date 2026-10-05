using System.Security.Cryptography;
using System.Text;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Time;

namespace BookingPlatform.Application.Common.Idempotency;

/// <summary>A validated Idempotency-Key plus the SHA-256 of method + route + canonical body (DATABASE.md §6).</summary>
public sealed record IdempotencyRequest(string Key, byte[] RequestHash)
{
    public const int MaxKeyLength = 100;

    public static IdempotencyRequest Create(string? key, string method, string route, string canonicalBody)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new BadRequestAppException(ErrorCodes.IdempotencyKeyRequired, "The Idempotency-Key header is required for this request.");
        }

        var trimmed = key.Trim();
        if (trimmed.Length > MaxKeyLength || trimmed.Any(char.IsControl))
        {
            throw new ValidationAppException("Idempotency-Key", $"The Idempotency-Key must be 1-{MaxKeyLength} printable characters.");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{method.ToUpperInvariant()} {route}\n{canonicalBody}"));
        return new IdempotencyRequest(trimmed, hash);
    }
}

/// <summary>A stored response returned instead of re-executing a request (<c>Idempotent-Replayed: true</c>).</summary>
public sealed record IdempotentReplay(int Status, string Body);

/// <summary>Either the fresh result of an operation or the replay of an earlier one with the same key.</summary>
public sealed record IdempotentResult<T>(T? Value, IdempotentReplay? Replay)
    where T : class
{
    public bool IsReplay => Replay is not null;
}

public sealed record IdempotencyRecord(byte[] RequestHash, string Status, int? ResponseStatus, string? ResponseBody, Guid? ResourceId);

public interface IIdempotencyStore
{
    /// <summary>
    /// INSERT ... ON CONFLICT DO NOTHING inside the current transaction. Returns false when the key already exists
    /// (a concurrent duplicate waits on the unique index until the first transaction finishes).
    /// </summary>
    Task<bool> TryBeginAsync(Guid tenantId, string scope, string key, byte[] requestHash, DateTime expiresAt, CancellationToken cancellationToken);

    Task<IdempotencyRecord?> GetAsync(Guid tenantId, string scope, string key, CancellationToken cancellationToken);

    Task CompleteAsync(Guid tenantId, string scope, string key, int responseStatus, string responseBody, Guid? resourceId, CancellationToken cancellationToken);
}

/// <summary>Serialises responses exactly as the API writes them, so replays are byte-for-byte consistent.</summary>
public interface IResponseSerializer
{
    string Serialize<T>(T value);
}

public sealed class IdempotencyGuard(IIdempotencyStore store, IResponseSerializer serializer, IClock clock)
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>Claims the key. Returns a replay when the same request already completed; throws on a mismatched payload.</summary>
    public async Task<IdempotentReplay?> BeginAsync(Guid tenantId, string scope, IdempotencyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await store.TryBeginAsync(tenantId, scope, request.Key, request.RequestHash, clock.UtcNow.Add(Retention), cancellationToken))
        {
            return null;
        }

        var existing = await store.GetAsync(tenantId, scope, request.Key, cancellationToken)
            ?? throw new ConflictAppException(ErrorCodes.IdempotencyConflict, "The idempotency key could not be claimed. Retry the request.");
        if (!CryptographicOperations.FixedTimeEquals(existing.RequestHash, request.RequestHash))
        {
            throw new ConflictAppException(ErrorCodes.IdempotencyConflict, "The idempotency key was already used with a different request.");
        }

        if (existing is not { Status: "COMPLETED", ResponseStatus: { } status, ResponseBody: { } body })
        {
            throw new ConflictAppException(ErrorCodes.IdempotencyConflict, "A request with this idempotency key is still in progress.");
        }

        return new IdempotentReplay(status, body);
    }

    /// <summary>Stores the response in the same transaction as the business change.</summary>
    public Task CompleteAsync<T>(Guid tenantId, string scope, IdempotencyRequest request, int status, T response, Guid? resourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return store.CompleteAsync(tenantId, scope, request.Key, status, serializer.Serialize(response), resourceId, cancellationToken);
    }
}

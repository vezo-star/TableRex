namespace BookingPlatform.Application.Common.Persistence;

/// <summary>
/// One database transaction per business operation. Repositories enlist automatically while a transaction is open,
/// so every write of an operation (entity, history, audit, outbox) commits or rolls back together.
/// </summary>
public interface IUnitOfWork
{
    bool HasActiveTransaction { get; }

    Task BeginAsync(CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    Task RollbackAsync(CancellationToken cancellationToken);

    /// <summary>Creates a savepoint inside the open transaction (used for auto-assignment retries).</summary>
    Task SavepointAsync(string name, CancellationToken cancellationToken);

    Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken);
}

public static class UnitOfWorkExtensions
{
    public static async Task<T> ExecuteAsync<T>(this IUnitOfWork unitOfWork, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(operation);

        await unitOfWork.BeginAsync(cancellationToken);
        try
        {
            var result = await operation();
            await unitOfWork.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public static Task ExecuteAsync(this IUnitOfWork unitOfWork, Func<Task> operation, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            await operation();
            return true;
        }, cancellationToken);
}

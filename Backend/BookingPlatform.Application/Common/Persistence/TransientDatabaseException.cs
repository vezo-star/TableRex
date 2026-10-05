namespace BookingPlatform.Application.Common.Persistence;

/// <summary>
/// A deadlock or serialization failure. The whole transaction was rolled back, so the operation may be retried
/// (DATABASE.md §4.5: retry once, then 409).
/// </summary>
public sealed class TransientDatabaseException(string message, Exception innerException) : Exception(message, innerException);

public static class TransientRetry
{
    /// <summary>Runs <paramref name="operation"/> (a complete unit of work); retries once after a transient database failure.</summary>
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Func<Exception> onExhausted)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onExhausted);

        try
        {
            return await operation();
        }
        catch (TransientDatabaseException)
        {
            try
            {
                return await operation();
            }
            catch (TransientDatabaseException)
            {
                throw onExhausted();
            }
        }
    }
}

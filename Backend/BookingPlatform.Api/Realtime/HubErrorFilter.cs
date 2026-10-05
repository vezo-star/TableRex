using Microsoft.AspNetCore.SignalR;

namespace BookingPlatform.Api.Realtime;

/// <summary>
/// Expected denials (<see cref="HubException"/>) reach the client unchanged; anything else is logged here and replaced
/// with a generic message. SignalR's own dispatcher logging is silenced in configuration because it reports every
/// <see cref="HubException"/> as an error.
/// </summary>
public sealed class HubErrorFilter(ILogger<HubErrorFilter> logger) : IHubFilter
{
    public const string UnavailableMessage = "UNAVAILABLE";

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        ArgumentNullException.ThrowIfNull(invocationContext);
        ArgumentNullException.ThrowIfNull(next);
        try
        {
            return await next(invocationContext);
        }
        catch (HubException)
        {
            throw;
        }
        catch (OperationCanceledException) when (invocationContext.Context.ConnectionAborted.IsCancellationRequested)
        {
            throw new HubException(UnavailableMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub method {HubMethod} failed", invocationContext.HubMethodName);
            throw new HubException(UnavailableMessage);
        }
    }
}

namespace BookingPlatform.Worker;

/// <summary>
/// Records host start and graceful shutdown.
/// </summary>
public sealed class WorkerLifetimeLogger(ILogger<WorkerLifetimeLogger> logger, IHostEnvironment environment) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker started ({Environment})", environment.EnvironmentName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker stopping gracefully");
        return Task.CompletedTask;
    }
}

namespace ChessApp.Backend.Services;

/// <summary>
/// Expires join requests nobody approved in time and ends gatherings whose
/// time window has passed.
/// </summary>
public class GatheringMaintenanceWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GatheringMaintenanceWorker> _logger;

    public GatheringMaintenanceWorker(IServiceScopeFactory scopeFactory, ILogger<GatheringMaintenanceWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var changed = await scope.ServiceProvider.GetRequiredService<IGatheringService>().ExpireStaleAsync();
                if (changed > 0)
                    _logger.LogInformation("Expired or ended {Count} join requests and gatherings", changed);
            }
            catch (OperationCanceledException)
            {
                // Shutting down
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gathering maintenance failed");
            }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }
}

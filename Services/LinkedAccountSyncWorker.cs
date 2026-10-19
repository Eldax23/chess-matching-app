using Microsoft.Extensions.Options;

namespace ChessApp.Backend.Services;

/// <summary>
/// Periodically refreshes ratings of linked accounts that haven't been
/// synced within each platform's SyncIntervalHours, one batch per tick.
/// </summary>
public class LinkedAccountSyncWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LichessOptions _lichessOptions;
    private readonly ChessComOptions _chessComOptions;
    private readonly ILogger<LinkedAccountSyncWorker> _logger;

    public LinkedAccountSyncWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<LichessOptions> lichessOptions,
        IOptions<ChessComOptions> chessComOptions,
        ILogger<LinkedAccountSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _lichessOptions = lichessOptions.Value;
        _chessComOptions = chessComOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            // Platforms sync independently: one being down doesn't block the other
            await SyncAsync("Lichess", sp => sp.GetRequiredService<ILichessIntegrationService>()
                .SyncStaleAccountsAsync(TimeSpan.FromHours(_lichessOptions.SyncIntervalHours), stoppingToken));
            await SyncAsync("chess.com", sp => sp.GetRequiredService<IChessComIntegrationService>()
                .SyncStaleAccountsAsync(TimeSpan.FromHours(_chessComOptions.SyncIntervalHours), stoppingToken));
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SyncAsync(string platform, Func<IServiceProvider, Task<int>> sync)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var synced = await sync(scope.ServiceProvider);
            if (synced > 0)
                _logger.LogInformation("Synced {Count} {Platform} accounts", synced, platform);
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            // Includes rate limiting: just try again next tick
            _logger.LogWarning(ex, "{Platform} rating sync failed", platform);
        }
    }
}

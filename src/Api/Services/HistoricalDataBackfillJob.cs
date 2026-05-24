namespace Stocky.Api.Services;

/// <summary>
/// Backfills daily historical price bars for every symbol that appears in any
/// <see cref="Transaction"/>. For each symbol the job determines the earliest
/// purchase date and pulls split-adjusted daily closes from the configured
/// <see cref="IMarketDataProvider"/> from that date through today, upserting
/// rows into <see cref="StockyDbContext.HistoricalPrices"/>. Runs on startup
/// and again every <c>MarketData:HistoryRefreshHours</c> (default 6h) so new
/// trading days are picked up as they close.
/// <para>
/// Intraday refresh of currently-held / transacted symbols every few seconds
/// is handled separately by <see cref="QuoteRefresher"/>.
/// </para>
/// </summary>
public sealed class HistoricalDataBackfillJob(
    IServiceProvider services,
    IConfiguration config,
    ILogger<HistoricalDataBackfillJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshHours = Math.Max(1, config.GetValue("MarketData:HistoryRefreshHours", 6));
        var interval = TimeSpan.FromHours(refreshHours);

        // Small startup delay so EF migrations / warm-up can finish first.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Historical backfill iteration failed");
            }
            try { await Task.Delay(interval, stoppingToken); } catch { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var refresher = scope.ServiceProvider.GetRequiredService<DataRefreshService>();
        await refresher.BackfillHistoricalOnceAsync(ct);
    }
}

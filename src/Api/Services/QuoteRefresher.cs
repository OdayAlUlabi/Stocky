using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// Periodic refresher: pulls quotes for every symbol that appears in any
/// holding or watchlist, writes a new PriceQuote row, and runs the alert
/// evaluator. Interval comes from MarketData:RefreshSeconds (default 60).
/// </summary>
public sealed class QuoteRefresher(
    IServiceProvider services,
    IConfiguration config,
    ILogger<QuoteRefresher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = config.GetValue("MarketData:RefreshSeconds", 60);
        var delay = TimeSpan.FromSeconds(Math.Max(seconds, 10));

        // Small startup delay so the API can warm up first.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Quote refresh iteration failed");
            }

            try { await Task.Delay(delay, stoppingToken); } catch { break; }
        }
    }

    private async Task RefreshOnceAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StockyDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
        var evaluator = scope.ServiceProvider.GetRequiredService<AlertEvaluator>();

        var symbols = await db.Holdings.Select(h => h.Symbol)
            .Union(db.WatchlistItems.Select(w => w.Symbol))
            .Distinct()
            .ToListAsync(ct);
        if (symbols.Count == 0) return;

        var quotes = await provider.GetQuotesAsync(symbols, ct);
        foreach (var q in quotes)
        {
            db.PriceQuotes.Add(new PriceQuote
            {
                Symbol = q.Symbol,
                Price = q.Price,
                Change = q.Change,
                ChangePercent = q.ChangePercent,
                AsOf = q.AsOf
            });
        }
        await db.SaveChangesAsync(ct);
        await evaluator.EvaluateAsync(quotes, ct);
        logger.LogInformation("Refreshed {Count} quotes", quotes.Count);
    }
}

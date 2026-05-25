using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

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
        var db = scope.ServiceProvider.GetRequiredService<StockyDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();

        // Earliest purchase date per symbol across all portfolios.
        var firstSeenBySymbol = await db.Transactions
            .Where(t => t.Symbol != null && t.Symbol != "")
            .GroupBy(t => t.Symbol!)
            .Select(g => new { Symbol = g.Key, First = g.Min(t => t.ExecutedAt) })
            .ToListAsync(ct);

        if (firstSeenBySymbol.Count == 0)
        {
            logger.LogDebug("Historical backfill: no transactions found, nothing to do.");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var symbols = firstSeenBySymbol.Select(x => x.Symbol.ToUpperInvariant()).ToList();

        // Find the earliest date we need across all symbols so we issue one
        // batched provider call; per-symbol gaps are then resolved locally.
        var globalStart = firstSeenBySymbol
            .Min(x => DateOnly.FromDateTime(x.First.UtcDateTime));

        IReadOnlyDictionary<string, IReadOnlyList<Dtos.DailyBarDto>> bars;
        try
        {
            bars = await provider.GetDailyBarsAsync(symbols, globalStart, today, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Historical backfill: provider GetDailyBarsAsync failed");
            return;
        }

        if (bars.Count == 0)
        {
            logger.LogDebug("Historical backfill: provider returned no bars (stub or unavailable).");
            return;
        }

        // Existing (Symbol, Date) keys so we only insert missing rows.
        var existing = await db.HistoricalPrices
            .Where(h => symbols.Contains(h.Symbol))
            .Select(h => new { h.Symbol, h.Date })
            .ToListAsync(ct);
        var existingSet = new HashSet<(string, DateOnly)>(
            existing.Select(e => (e.Symbol.ToUpperInvariant(), e.Date)));

        var firstByUpper = firstSeenBySymbol.ToDictionary(
            x => x.Symbol.ToUpperInvariant(),
            x => DateOnly.FromDateTime(x.First.UtcDateTime));

        int inserted = 0;
        foreach (var (sym, list) in bars)
        {
            var upper = sym.ToUpperInvariant();
            if (!firstByUpper.TryGetValue(upper, out var symStart)) continue;
            foreach (var bar in list)
            {
                if (bar.Date < symStart) continue;
                if (existingSet.Contains((upper, bar.Date))) continue;
                db.HistoricalPrices.Add(new HistoricalPrice
                {
                    Symbol = upper,
                    Date = bar.Date,
                    Close = bar.Close,
                    Open = bar.Open,
                    High = bar.High,
                    Low = bar.Low,
                    Volume = bar.Volume,
                    Source = "provider",
                    CapturedAt = DateTimeOffset.UtcNow
                });
                inserted++;
            }
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Historical backfill: inserted {Count} new daily bars across {Symbols} symbols.",
                inserted, symbols.Count);
        }
        else
        {
            logger.LogDebug("Historical backfill: up-to-date ({Symbols} symbols).", symbols.Count);
        }
    }
}

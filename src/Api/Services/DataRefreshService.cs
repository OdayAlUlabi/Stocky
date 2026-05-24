using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// Shared implementation of the two market-data refresh passes. Both the
/// periodic <see cref="QuoteRefresher"/> / <see cref="HistoricalDataBackfillJob"/>
/// background services and the <c>AdminRefreshController</c> on-demand endpoints
/// delegate here so behaviour is identical regardless of trigger.
/// </summary>
public sealed class DataRefreshService(
    StockyDbContext db,
    IMarketDataProvider provider,
    AlertEvaluator evaluator,
    PriceTickBroadcaster? broadcaster,
    ILogger<DataRefreshService> logger)
{
    public sealed record QuoteRefreshResult(int Symbols, int Quotes);
    public sealed record HistoryBackfillResult(int Symbols, int Inserted, DateOnly? GlobalStart, DateOnly Today);

    /// <summary>
    /// Pulls a current quote for every symbol referenced by holdings, watchlists,
    /// or transactions, writes a new <see cref="PriceQuote"/> row, runs the
    /// alert evaluator, and fans out a real-time tick to SignalR subscribers.
    /// </summary>
    public async Task<QuoteRefreshResult> RefreshQuotesOnceAsync(CancellationToken ct)
    {
        var symbols = await db.Holdings.Select(h => h.Symbol)
            .Union(db.WatchlistItems.Select(w => w.Symbol))
            .Union(db.Transactions
                .Where(t => t.Symbol != null && t.Symbol != "")
                .Select(t => t.Symbol!))
            .Distinct()
            .ToListAsync(ct);
        if (symbols.Count == 0) return new QuoteRefreshResult(0, 0);

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

        if (broadcaster is not null)
        {
            try { await broadcaster.BroadcastAsync(quotes, ct); }
            catch (Exception ex) { logger.LogDebug(ex, "PriceTick broadcast failed"); }
        }

        logger.LogInformation("Refreshed {Count} quotes", quotes.Count);
        return new QuoteRefreshResult(symbols.Count, quotes.Count);
    }

    /// <summary>
    /// For every distinct transaction symbol, pulls split-adjusted daily bars
    /// from each symbol's earliest <c>Transaction.ExecutedAt</c> through today
    /// and upserts the missing rows into <see cref="StockyDbContext.HistoricalPrices"/>.
    /// </summary>
    public async Task<HistoryBackfillResult> BackfillHistoricalOnceAsync(CancellationToken ct)
    {
        var firstSeenBySymbol = await db.Transactions
            .Where(t => t.Symbol != null && t.Symbol != "")
            .GroupBy(t => t.Symbol!)
            .Select(g => new { Symbol = g.Key, First = g.Min(t => t.ExecutedAt) })
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (firstSeenBySymbol.Count == 0)
        {
            logger.LogDebug("Historical backfill: no transactions found, nothing to do.");
            return new HistoryBackfillResult(0, 0, null, today);
        }

        var symbols = firstSeenBySymbol.Select(x => x.Symbol.ToUpperInvariant()).ToList();
        var globalStart = firstSeenBySymbol.Min(x => DateOnly.FromDateTime(x.First.UtcDateTime));

        IReadOnlyDictionary<string, IReadOnlyList<Dtos.DailyBarDto>> bars;
        try
        {
            bars = await provider.GetDailyBarsAsync(symbols, globalStart, today, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Historical backfill: provider GetDailyBarsAsync failed");
            return new HistoryBackfillResult(symbols.Count, 0, globalStart, today);
        }

        if (bars.Count == 0)
        {
            logger.LogDebug("Historical backfill: provider returned no bars (stub or unavailable).");
            return new HistoryBackfillResult(symbols.Count, 0, globalStart, today);
        }

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

        return new HistoryBackfillResult(symbols.Count, inserted, globalStart, today);
    }
}

using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;

namespace Stocky.Api.Services;

/// <summary>
/// On-demand data refresh used by the admin force-refresh endpoint. Mirrors
/// the logic in <see cref="QuoteRefresher"/> and <see cref="HistoricalDataBackfillJob"/>
/// so it can be invoked any time (including off-hours / weekends) regardless
/// of <c>MarketData:AlwaysRefresh</c> or the market-open schedule.
/// </summary>
public sealed class DataRefreshService(
    StockyDbContext db,
    IMarketDataProvider provider,
    AlertEvaluator evaluator,
    PriceTickBroadcaster? broadcaster,
    ILogger<DataRefreshService> logger)
{
    public async Task<QuoteRefreshResult> RefreshQuotesOnceAsync(CancellationToken ct)
    {
        var symbols = await db.Holdings.Select(h => h.Symbol)
            .Union(db.WatchlistItems.Select(w => w.Symbol))
            .Union(db.Transactions
                .Where(t => t.Symbol != null && t.Symbol != "")
                .Select(t => t.Symbol!))
            .Distinct()
            .ToListAsync(ct);

        if (symbols.Count == 0)
        {
            return new QuoteRefreshResult(0, 0);
        }

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

        logger.LogInformation("Force-refresh: refreshed {Count} quotes across {Symbols} symbols",
            quotes.Count, symbols.Count);
        return new QuoteRefreshResult(symbols.Count, quotes.Count);
    }

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
            return new HistoryBackfillResult(0, 0, null, today);
        }

        var symbols = firstSeenBySymbol.Select(x => x.Symbol.ToUpperInvariant()).ToList();
        var globalStart = firstSeenBySymbol
            .Min(x => DateOnly.FromDateTime(x.First.UtcDateTime));

        IReadOnlyDictionary<string, IReadOnlyList<Dtos.DailyBarDto>> bars;
        try
        {
            bars = await provider.GetDailyBarsAsync(symbols, globalStart, today, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Force-refresh: provider GetDailyBarsAsync failed");
            return new HistoryBackfillResult(symbols.Count, 0, globalStart, today);
        }

        if (bars.Count == 0)
        {
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
                "Force-refresh: inserted {Count} new daily bars across {Symbols} symbols.",
                inserted, symbols.Count);
        }

        return new HistoryBackfillResult(symbols.Count, inserted, globalStart, today);
    }
}

public readonly record struct QuoteRefreshResult(int Symbols, int Quotes);
public readonly record struct HistoryBackfillResult(int Symbols, int Inserted, DateOnly? GlobalStart, DateOnly Today);

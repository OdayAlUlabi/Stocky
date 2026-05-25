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
    PortfolioLedgerService ledger,
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
            return new QuoteRefreshResult(0, 0, Array.Empty<PortfolioValueSnapshot>());
        }

        var quotes = await provider.GetQuotesAsync(symbols, ct);

        // Dedupe: skip writing a new PriceQuote row if the latest existing row
        // for the same symbol has identical Price/Change/ChangePercent.
        var quoteSymbols = quotes.Select(q => q.Symbol).ToList();
        var latestExisting = await db.PriceQuotes
            .Where(p => quoteSymbols.Contains(p.Symbol))
            .GroupBy(p => p.Symbol)
            .Select(g => g.OrderByDescending(p => p.AsOf).First())
            .ToDictionaryAsync(p => p.Symbol, ct);

        int added = 0;
        foreach (var q in quotes)
        {
            if (latestExisting.TryGetValue(q.Symbol, out var prev) &&
                prev.Price == q.Price &&
                prev.Change == q.Change &&
                prev.ChangePercent == q.ChangePercent)
            {
                continue;
            }
            db.PriceQuotes.Add(new PriceQuote
            {
                Symbol = q.Symbol,
                Price = q.Price,
                Change = q.Change,
                ChangePercent = q.ChangePercent,
                AsOf = q.AsOf
            });
            added++;
        }
        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        await evaluator.EvaluateAsync(quotes, ct);

        if (broadcaster is not null)
        {
            try { await broadcaster.BroadcastAsync(quotes, ct); }
            catch (Exception ex) { logger.LogDebug(ex, "PriceTick broadcast failed"); }
        }

        var portfolioValues = await RefreshPortfolioSnapshotsAsync(ct);

        logger.LogInformation("Force-refresh: refreshed {Count} quotes across {Symbols} symbols; updated {Portfolios} portfolio snapshots",
            quotes.Count, symbols.Count, portfolioValues.Count);
        return new QuoteRefreshResult(symbols.Count, quotes.Count, portfolioValues);
    }

    /// <summary>
    /// Recompute today's PortfolioSnapshot rows from the latest PriceQuote per symbol
    /// and return a current market-value summary for every portfolio. Mirrors the logic
    /// in <see cref="SnapshotJob"/> so portfolio values reflect the freshly-pulled
    /// prices immediately after a force-refresh, instead of waiting for the next
    /// 6-hourly snapshot run.
    /// </summary>
    public async Task<IReadOnlyList<PortfolioValueSnapshot>> RefreshPortfolioSnapshotsAsync(CancellationToken ct)
    {
        var portfolios = await db.Portfolios.Include(p => p.Holdings).ToListAsync(ct);
        if (portfolios.Count == 0) return Array.Empty<PortfolioValueSnapshot>();

        var symbols = portfolios.SelectMany(p => p.Holdings.Select(h => h.Symbol)).Distinct().ToList();
        var latest = symbols.Count == 0
            ? new Dictionary<string, PriceQuote>()
            : await db.PriceQuotes
                .Where(q => symbols.Contains(q.Symbol))
                .GroupBy(q => q.Symbol)
                .Select(g => g.OrderByDescending(x => x.AsOf).First())
                .ToDictionaryAsync(q => q.Symbol, q => q, ct);

        // Fallback: latest available daily close per symbol (used when no intraday
        // PriceQuote exists, e.g. weekend / off-hours / never-quoted symbols).
        var symbolsUpper = symbols.Select(s => s.ToUpperInvariant()).ToList();
        var latestHist = symbolsUpper.Count == 0
            ? new Dictionary<string, HistoricalPrice>()
            : await db.HistoricalPrices
                .Where(h => symbolsUpper.Contains(h.Symbol))
                .GroupBy(h => h.Symbol)
                .Select(g => g.OrderByDescending(x => x.Date).First())
                .ToDictionaryAsync(h => h.Symbol, h => h, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var existing = await db.PortfolioSnapshots
            .Where(s => s.Date == today)
            .ToDictionaryAsync(s => s.PortfolioId, s => s, ct);

        var results = new List<PortfolioValueSnapshot>(portfolios.Count);
        foreach (var p in portfolios)
        {
            decimal mv = 0m, cb = 0m, dayPnl = 0m;
            foreach (var h in p.Holdings)
            {
                cb += h.Quantity * h.AverageCost;
                if (latest.TryGetValue(h.Symbol, out var q))
                {
                    mv += h.Quantity * q.Price;
                    if (q.Change.HasValue) dayPnl += h.Quantity * q.Change.Value;
                }
                else if (latestHist.TryGetValue(h.Symbol.ToUpperInvariant(), out var hp))
                {
                    // No live quote — use the last available daily close.
                    mv += h.Quantity * hp.Close;
                }
            }
            if (existing.TryGetValue(p.Id, out var snap))
            {
                snap.MarketValue = mv;
                snap.CostBasis = cb;
                snap.DayPnL = dayPnl;
            }
            else
            {
                db.PortfolioSnapshots.Add(new PortfolioSnapshot
                {
                    PortfolioId = p.Id,
                    Date = today,
                    MarketValue = mv,
                    CostBasis = cb,
                    DayPnL = dayPnl
                });
            }

            var cash = await ledger.GetCashBalanceAsync(p.Id, ct);
            results.Add(new PortfolioValueSnapshot(
                p.Id, p.Name, p.BaseCurrency,
                Math.Round(mv, 2),
                Math.Round(cb, 2),
                Math.Round(mv - cb, 2),
                cash,
                Math.Round(mv + cash, 2)));
        }

        await db.SaveChangesAsync(ct);
        return results;
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

public readonly record struct QuoteRefreshResult(int Symbols, int Quotes, IReadOnlyList<PortfolioValueSnapshot> Portfolios);
public readonly record struct HistoryBackfillResult(int Symbols, int Inserted, DateOnly? GlobalStart, DateOnly Today);

public sealed record PortfolioValueSnapshot(
    Guid PortfolioId,
    string Name,
    string Currency,
    decimal MarketValue,
    decimal CostBasis,
    decimal UnrealizedPnL,
    decimal CashBalance,
    decimal TotalEquity);

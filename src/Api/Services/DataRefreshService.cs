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
    HoldingsCalculator holdingsCalc,
    PriceTickBroadcaster? broadcaster,
    ILogger<DataRefreshService> logger)
{
    // Parallel fan-out: how many symbols to bundle into one provider call,
    // and how many of those bundles to fire concurrently. Alpaca's
    // multi-symbol endpoints are cheaper per call than N single-symbol calls,
    // but a single mega-batch (a) hits URL-length limits and (b) gives no
    // parallelism on the wire. ~25 symbols/chunk × 4 in flight = good
    // throughput on hundreds of symbols without thundering-herd risk.
    private const int ProviderChunkSize = 25;
    private const int ProviderMaxParallel = 4;

    public async Task<QuoteRefreshResult> RefreshQuotesOnceAsync(CancellationToken ct)
    {
        var rawSymbols = await db.Holdings.Select(h => h.Symbol)
            .Union(db.WatchlistItems.Select(w => w.Symbol))
            .Union(db.Transactions
                .Where(t => t.Symbol != null && t.Symbol != "")
                .Select(t => t.Symbol!))
            .ToListAsync(ct);

        var symbols = rawSymbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (symbols.Count == 0)
        {
            return new QuoteRefreshResult(0, 0, Array.Empty<PortfolioValueSnapshot>());
        }

        IReadOnlyList<Stocky.Api.Dtos.QuoteDto> quotes;
        try
        {
            quotes = await FetchQuotesInParallelAsync(symbols, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Force-refresh: provider GetQuotesAsync failed for {Count} symbols; skipping unavailable quotes",
                symbols.Count);
            quotes = Array.Empty<Stocky.Api.Dtos.QuoteDto>();
        }

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
    /// Compute a live portfolio value summary for every portfolio from the
    /// latest <see cref="PriceQuote"/> per symbol (falling back to the most
    /// recent <see cref="HistoricalPrice"/> close when no intraday quote
    /// exists). Pure read — does NOT persist anything to
    /// <see cref="StockyDbContext.PortfolioSnapshots"/>. The periodic
    /// <see cref="SnapshotJob"/> is the single source of truth for stored
    /// snapshots used by <c>PerformanceController</c>'s TWR window.
    /// </summary>
    public async Task<IReadOnlyList<PortfolioValueSnapshot>> RefreshPortfolioSnapshotsAsync(CancellationToken ct)
    {
        var portfolios = await db.Portfolios.ToListAsync(ct);
        if (portfolios.Count == 0) return Array.Empty<PortfolioValueSnapshot>();

        // Derive current holdings live from the transaction journal — do not
        // read from db.Holdings.
        var holdings = await holdingsCalc.ComputeManyAsync(
            portfolios.Select(p => p.Id).ToList(), ct);
        var holdingsByPortfolio = holdings
            .GroupBy(h => h.PortfolioId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Holding>)g.ToList());

        var symbols = holdings.Select(h => h.Symbol).Distinct().ToList();
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

        var results = new List<PortfolioValueSnapshot>(portfolios.Count);
        foreach (var p in portfolios)
        {
            decimal mv = 0m, cb = 0m;
            var portfolioHoldings = holdingsByPortfolio.TryGetValue(p.Id, out var hs)
                ? hs
                : (IReadOnlyList<Holding>)Array.Empty<Holding>();
            foreach (var h in portfolioHoldings)
            {
                cb += h.Quantity * h.AverageCost;
                if (latest.TryGetValue(h.Symbol, out var q))
                {
                    mv += h.Quantity * q.Price;
                }
                else if (latestHist.TryGetValue(h.Symbol.ToUpperInvariant(), out var hp))
                {
                    // No live quote — use the last available daily close.
                    mv += h.Quantity * hp.Close;
                }
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
            bars = await FetchBarsInParallelAsync(symbols, globalStart, today, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Force-refresh: provider GetDailyBarsAsync failed");
            return new HistoryBackfillResult(symbols.Count, 0, globalStart, today);
        }

        logger.LogInformation(
            "Force-refresh history: provider returned bars for {Returned}/{Requested} symbols (window {From}..{To}): {Detail}",
            bars.Count(kv => kv.Value.Count > 0), symbols.Count, globalStart, today,
            string.Join(", ", symbols.Select(s => $"{s}=" + (bars.TryGetValue(s, out var list) ? list.Count : 0))));

        if (bars.Count == 0)
        {
            return new HistoryBackfillResult(symbols.Count, 0, globalStart, today);
        }

        var existing = await db.HistoricalPrices
            .Where(h => symbols.Contains(h.Symbol))
            .ToListAsync(ct);
        var existingByKey = existing.ToDictionary(
            e => (e.Symbol.ToUpperInvariant(), e.Date),
            e => e);

        var firstByUpper = firstSeenBySymbol.ToDictionary(
            x => x.Symbol.ToUpperInvariant(),
            x => DateOnly.FromDateTime(x.First.UtcDateTime));

        int inserted = 0;
        int updated = 0;
        foreach (var (sym, list) in bars)
        {
            var upper = sym.ToUpperInvariant();
            if (!firstByUpper.TryGetValue(upper, out var symStart)) continue;
            foreach (var bar in list)
            {
                if (bar.Date < symStart) continue;
                if (existingByKey.TryGetValue((upper, bar.Date), out var row))
                {
                    // Backfill OHLCV onto pre-existing rows that were saved before
                    // the provider returned the full bar shape.
                    var dirty = false;
                    if (row.Open is null && bar.Open is not null) { row.Open = bar.Open; dirty = true; }
                    if (row.High is null && bar.High is not null) { row.High = bar.High; dirty = true; }
                    if (row.Low is null && bar.Low is not null) { row.Low = bar.Low; dirty = true; }
                    if (row.Volume is null && bar.Volume is not null) { row.Volume = bar.Volume; dirty = true; }
                    if (dirty) updated++;
                    continue;
                }
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

        if (inserted > 0 || updated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Force-refresh: inserted {Inserted} new daily bars, backfilled OHLCV on {Updated} existing rows across {Symbols} symbols.",
                inserted, updated, symbols.Count);
        }

        return new HistoryBackfillResult(symbols.Count, inserted, globalStart, today);
    }

    /// <summary>
    /// Snapshot of how much daily-close coverage exists in HistoricalPrices for every
    /// symbol that has at least one transaction. Used by the admin Data Refresh page
    /// so users can verify "from each transaction date through today" is fully filled in.
    /// </summary>
    public async Task<IReadOnlyList<HistoricalCoverageRow>> GetHistoricalCoverageAsync(CancellationToken ct)
    {
        var firstSeen = await db.Transactions
            .Where(t => t.Symbol != null && t.Symbol != "")
            .GroupBy(t => t.Symbol!)
            .Select(g => new { Symbol = g.Key, First = g.Min(t => t.ExecutedAt) })
            .ToListAsync(ct);
        if (firstSeen.Count == 0) return Array.Empty<HistoricalCoverageRow>();

        var symbols = firstSeen.Select(x => x.Symbol.ToUpperInvariant()).ToList();
        var coverage = await db.HistoricalPrices
            .Where(h => symbols.Contains(h.Symbol))
            .GroupBy(h => h.Symbol)
            .Select(g => new { Symbol = g.Key, Rows = g.Count(), MinDate = g.Min(x => x.Date), MaxDate = g.Max(x => x.Date) })
            .ToListAsync(ct);
        var byUpper = coverage.ToDictionary(c => c.Symbol.ToUpperInvariant());

        return firstSeen
            .Select(f =>
            {
                var upper = f.Symbol.ToUpperInvariant();
                var firstTx = DateOnly.FromDateTime(f.First.UtcDateTime);
                if (byUpper.TryGetValue(upper, out var c))
                    return new HistoricalCoverageRow(upper, c.Rows, c.MinDate, c.MaxDate, firstTx);
                return new HistoricalCoverageRow(upper, 0, null, null, firstTx);
            })
            .OrderBy(r => r.Symbol)
            .ToList();
    }

    /// <summary>
    /// Splits <paramref name="symbols"/> into chunks of <see cref="ProviderChunkSize"/>
    /// and calls <see cref="IMarketDataProvider.GetQuotesAsync"/> concurrently
    /// (up to <see cref="ProviderMaxParallel"/> in flight) so a refresh of N
    /// symbols completes in roughly ceil(N/chunk)/parallel batches instead of
    /// one serial mega-call.
    /// </summary>
    private async Task<IReadOnlyList<Stocky.Api.Dtos.QuoteDto>> FetchQuotesInParallelAsync(
        IReadOnlyList<string> symbols, CancellationToken ct)
    {
        var chunks = Chunk(symbols, ProviderChunkSize);
        var bag = new System.Collections.Concurrent.ConcurrentBag<Stocky.Api.Dtos.QuoteDto>();
        using var gate = new SemaphoreSlim(ProviderMaxParallel, ProviderMaxParallel);

        var tasks = chunks.Select(async chunk =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var part = await provider.GetQuotesAsync(chunk, ct);
                foreach (var q in part) bag.Add(q);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Force-refresh: GetQuotesAsync failed for chunk of {Count} symbols", chunk.Count);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return bag.ToList();
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<Dtos.DailyBarDto>>> FetchBarsInParallelAsync(
        IReadOnlyList<string> symbols, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var chunks = Chunk(symbols, ProviderChunkSize);
        var merged = new System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<Dtos.DailyBarDto>>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(ProviderMaxParallel, ProviderMaxParallel);

        var tasks = chunks.Select(async chunk =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var part = await provider.GetDailyBarsAsync(chunk, from, to, ct);
                foreach (var kv in part)
                {
                    merged[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Force-refresh: GetDailyBarsAsync failed for chunk of {Count} symbols ({From}..{To})",
                    chunk.Count, from, to);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return merged.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static List<List<string>> Chunk(IReadOnlyList<string> source, int size)
    {
        var result = new List<List<string>>((source.Count + size - 1) / size);
        for (int i = 0; i < source.Count; i += size)
        {
            var len = Math.Min(size, source.Count - i);
            var part = new List<string>(len);
            for (int j = 0; j < len; j++) part.Add(source[i + j]);
            result.Add(part);
        }
        return result;
    }
}

public readonly record struct QuoteRefreshResult(int Symbols, int Quotes, IReadOnlyList<PortfolioValueSnapshot> Portfolios);
public readonly record struct HistoryBackfillResult(int Symbols, int Inserted, DateOnly? GlobalStart, DateOnly Today);

public sealed record HistoricalCoverageRow(string Symbol, int Rows, DateOnly? MinDate, DateOnly? MaxDate, DateOnly? FirstTransaction);

public sealed record PortfolioValueSnapshot(
    Guid PortfolioId,
    string Name,
    string Currency,
    decimal MarketValue,
    decimal CostBasis,
    decimal UnrealizedPnL,
    decimal CashBalance,
    decimal TotalEquity);

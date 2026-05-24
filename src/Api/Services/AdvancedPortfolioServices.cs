using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Cross-sectional momentum scoring (GitHub issue 1.3).
/// For each symbol in the universe:
///   1. Pull daily closes over max(lookback) + buffer.
///   2. Compute trailing return over each lookback window; optionally skip
///      the most recent ~21 trading days (academic momentum convention to
///      avoid the well-known short-term reversal effect).
///   3. Optionally volatility-scale each window return by realised vol over
///      the same window.
///   4. Composite = average across windows; final score = percentile rank
///      across the universe in [0, 100].
/// </summary>
public sealed class MomentumScoringService(IMarketDataProvider market, IAdvancedMarketDataProvider advanced)
{
    public async Task<MomentumScoreSetDto?> ScoreAsync(MomentumRequest req, CancellationToken ct = default)
    {
        var symbols = (req.Universe ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        if (symbols.Count == 0) return null;
        var windows = (req.LookbackDays ?? Array.Empty<int>())
            .Where(w => w > 0).Distinct().OrderBy(w => w).ToList();
        if (windows.Count == 0) windows = new() { 21, 63, 126, 252 };

        var skip = req.SkipLatestMonth ? 21 : 0;
        var maxWindow = windows.Max();
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-(maxWindow + skip + 14));

        var closes = new Dictionary<string, IReadOnlyList<DailyBarDto>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var bulk = await market.GetDailyBarsAsync(symbols, from, to, ct);
            foreach (var kv in bulk) closes[kv.Key] = kv.Value;
        }
        catch { }
        foreach (var sym in symbols)
        {
            if (closes.TryGetValue(sym, out var have) && have.Count >= 2) continue;
            var ohlc = await advanced.GetOhlcAsync(sym, from, to, ct);
            closes[sym] = ohlc.Select(b => new DailyBarDto(b.Date, b.Close)).ToList();
        }

        // Per-symbol window returns and vol-adjusted returns.
        var rows = new List<(string Symbol, Dictionary<int, decimal> Ret, Dictionary<int, decimal> Adj, decimal Composite)>();
        foreach (var sym in symbols)
        {
            var bars = closes.TryGetValue(sym, out var b) ? b.OrderBy(x => x.Date).ToList() : new List<DailyBarDto>();
            if (bars.Count < skip + 5)
            {
                rows.Add((sym, new(), new(), 0m));
                continue;
            }
            int endIdx = bars.Count - 1 - skip; // last bar to use for momentum
            if (endIdx < 1) endIdx = bars.Count - 1;
            var endPrice = (double)bars[endIdx].Close;
            var ret = new Dictionary<int, decimal>();
            var adj = new Dictionary<int, decimal>();
            var composites = new List<double>();
            foreach (var w in windows)
            {
                int startIdx = endIdx - w;
                if (startIdx < 0 || (double)bars[startIdx].Close <= 0)
                {
                    ret[w] = 0; adj[w] = 0; continue;
                }
                var startPrice = (double)bars[startIdx].Close;
                var r = endPrice / startPrice - 1.0;
                ret[w] = (decimal)Math.Round(r * 100, 4);

                if (req.VolatilityScale)
                {
                    double sumSq = 0; int cnt = 0; double prev = startPrice;
                    for (int i = startIdx + 1; i <= endIdx; i++)
                    {
                        var c = (double)bars[i].Close;
                        if (prev > 0 && c > 0)
                        {
                            var lr = Math.Log(c / prev);
                            sumSq += lr * lr; cnt++;
                        }
                        prev = c;
                    }
                    var vol = cnt > 0 ? Math.Sqrt(sumSq / cnt) * Math.Sqrt(252.0) : 0;
                    var rAdj = vol > 1e-6 ? r / vol : r;
                    adj[w] = (decimal)Math.Round(rAdj, 6);
                    composites.Add(rAdj);
                }
                else
                {
                    adj[w] = ret[w];
                    composites.Add(r);
                }
            }
            var comp = composites.Count == 0 ? 0 : composites.Average();
            rows.Add((sym, ret, adj, (decimal)comp));
        }

        // Percentile rank across universe.
        var ordered = rows.OrderBy(r => r.Composite).ToList();
        var nRows = ordered.Count;
        var scores = new List<MomentumScoreDto>(nRows);
        for (int i = 0; i < nRows; i++)
        {
            var r = ordered[i];
            var pct = nRows == 1 ? 50m : (decimal)Math.Round(100.0 * i / (nRows - 1), 2);
            scores.Add(new MomentumScoreDto(
                Symbol: r.Symbol,
                CompositeScore: pct,
                WindowReturns: r.Ret,
                WindowVolAdjusted: r.Adj,
                Rank: 0));   // filled below
        }
        // Rank 1 = highest score.
        var ranked = scores.OrderByDescending(s => s.CompositeScore)
            .Select((s, i) => s with { Rank = i + 1 })
            .ToList();
        return new MomentumScoreSetDto(to, symbols.Count, windows, ranked);
    }
}

/// <summary>
/// Pure-math position sizing (GitHub issue 4.3). No DB, no market data.
/// Provides three independent sizing recommendations:
///   - Fixed-fractional (risk-per-trade against stop): shares such that
///     loss-at-stop ≈ account × risk%. Industry-standard primary method.
///   - Kelly criterion: f* = (p·b − q) / b, capped at half-Kelly for prudence.
///   - Volatility targeting: shares = (account × target_vol / asset_vol) / entry.
/// The recommended method is the *smallest* non-zero sizing — always the
/// most conservative — so callers can override but defaults are safe.
/// </summary>
public sealed class PositionSizingService
{
    public PositionSizingResultDto Compute(PositionSizingRequest req)
    {
        var account = Math.Max(0m, req.AccountSize);
        var entry = Math.Max(0.01m, req.EntryPrice);
        var risk = Math.Clamp(req.RiskPerTradePercent, 0m, 0.50m);
        var riskDollars = Math.Round(account * risk, 2);

        // ---- Fixed-fractional / risk-per-trade ----
        int ffShares = 0;
        if (req.StopLossPrice > 0 && req.StopLossPrice < entry)
        {
            var perShareRisk = entry - req.StopLossPrice;
            if (perShareRisk > 0)
                ffShares = (int)Math.Floor(riskDollars / perShareRisk);
        }
        ffShares = Math.Max(0, ffShares);
        // Cap so notional cannot exceed account size.
        var maxByAccount = (int)Math.Floor(account / entry);
        ffShares = Math.Min(ffShares, maxByAccount);

        // ---- Kelly ----
        int kellyShares = 0;
        decimal kellyFraction = 0m;
        if (req.WinRate is decimal p && req.AvgWinDollars is decimal w && req.AvgLossDollars is decimal l
            && p > 0 && p < 1 && w > 0 && l > 0)
        {
            var b = w / l;                                   // payoff ratio
            var q = 1m - p;
            var fStar = (p * b - q) / b;                     // raw Kelly
            kellyFraction = Math.Round(Math.Max(0m, fStar) / 2m, 4);  // half-Kelly cap
            kellyShares = (int)Math.Floor(account * kellyFraction / entry);
        }
        kellyShares = Math.Max(0, kellyShares);

        // ---- Volatility targeting ----
        int volShares = 0;
        if (req.AssetVolatilityPercent is decimal av && av > 0 && req.TargetVolatilityPercent is decimal tv && tv > 0)
        {
            var notional = account * (tv / av);
            volShares = (int)Math.Floor(Math.Max(0m, notional) / entry);
        }
        volShares = Math.Max(0, volShares);

        // Pick smallest non-zero method; if all zero, use fixed-fractional (0).
        var candidates = new List<(int Shares, string Method)>
        {
            (ffShares, "fixed-fractional"),
            (kellyShares, "kelly-half"),
            (volShares, "volatility-target"),
        };
        var nonZero = candidates.Where(c => c.Shares > 0).ToList();
        (int Shares, string Method) recommended = nonZero.Count > 0
            ? nonZero.OrderBy(c => c.Shares).First()
            : (ffShares, "fixed-fractional");

        string rationale = recommended.Shares == 0
            ? "All sizing methods returned zero. Provide a valid stop-loss below entry, Kelly inputs, or volatility inputs."
            : $"Most conservative non-zero recommendation across {nonZero.Count} method(s): "
              + $"fixed-fractional={ffShares}, kelly={kellyShares}, vol-target={volShares}.";

        return new PositionSizingResultDto(
            AccountSize: account,
            EntryPrice: entry,
            RiskPerTradeDollars: riskDollars,
            FixedFractionalShares: ffShares,
            FixedFractionalNotional: Math.Round(ffShares * entry, 2),
            KellyShares: kellyShares,
            KellyFraction: kellyFraction,
            KellyNotional: Math.Round(kellyShares * entry, 2),
            VolTargetShares: volShares,
            VolTargetNotional: Math.Round(volShares * entry, 2),
            RecommendedShares: recommended.Shares,
            RecommendedMethod: recommended.Method,
            Rationale: rationale);
    }
}

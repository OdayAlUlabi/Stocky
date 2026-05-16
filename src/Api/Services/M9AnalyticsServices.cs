using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// M9 #23. Extended risk metrics — Sharpe, Sortino, max drawdown, historical
/// VaR/CVaR at the 95% and 99% confidence levels, annualised total/downside
/// volatility, and alpha/beta vs the configured benchmark. Built on top of
/// <see cref="PortfolioAnalyticsService"/> so the underlying TWRR daily
/// returns and the drawdown calc stay the single source of truth.
/// </summary>
public sealed class RiskMetricsService(
    StockyDbContext db,
    PortfolioAnalyticsService analytics,
    IMarketDataProvider market)
{
    public async Task<RiskMetricsDto?> BuildAsync(Guid portfolioId, string ownerId, CancellationToken ct = default)
    {
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return null;
        var a = await analytics.BuildAsync(portfolioId, ownerId, ct);
        if (a is null) return null;

        var benchmark = string.IsNullOrWhiteSpace(portfolio.BenchmarkSymbol) ? "SPY" : portfolio.BenchmarkSymbol!;
        var daily = a.DailyReturnSeries;
        if (daily.Count == 0)
        {
            return new RiskMetricsDto(portfolioId, a.From, a.To,
                0, 0, 0, a.To, 0, 0, 0, 0, 0, 0, 0, benchmark);
        }

        // Convert to decimal-as-fraction returns (DailyReturnPointDto is in percent).
        var rets = daily.Select(d => (double)d.ReturnPercent / 100.0).ToArray();
        var mean = rets.Average();
        var sq = rets.Sum(r => (r - mean) * (r - mean));
        var stdev = rets.Length > 1 ? Math.Sqrt(sq / (rets.Length - 1)) : 0;
        var annualVol = stdev * Math.Sqrt(252);

        // Downside deviation: stdev of returns below 0 (MAR=0 by convention).
        var downside = rets.Where(r => r < 0).ToArray();
        var downStdev = downside.Length > 1
            ? Math.Sqrt(downside.Sum(r => r * r) / downside.Length)
            : 0;
        var annualDownVol = downStdev * Math.Sqrt(252);

        var sharpe = stdev > 0 ? (mean * 252) / annualVol : 0;
        var sortino = downStdev > 0 ? (mean * 252) / annualDownVol : 0;

        // Historical VaR — sort returns ascending, pick the p-th worst.
        var sorted = rets.OrderBy(r => r).ToArray();
        double Var(double p)
        {
            if (sorted.Length == 0) return 0;
            var idx = (int)Math.Floor((1 - p) * sorted.Length);
            if (idx < 0) idx = 0;
            if (idx >= sorted.Length) idx = sorted.Length - 1;
            return -sorted[idx]; // express as positive loss magnitude
        }
        double Cvar(double p)
        {
            if (sorted.Length == 0) return 0;
            var cutoff = (int)Math.Floor((1 - p) * sorted.Length);
            if (cutoff < 1) cutoff = 1;
            var tail = sorted.Take(cutoff).ToArray();
            return tail.Length == 0 ? 0 : -tail.Average();
        }

        var var95 = Var(0.95);
        var var99 = Var(0.99);
        var cvar95 = Cvar(0.95);

        // Alpha vs benchmark: portfolio return - (rf + beta * (bench - rf)). rf=0.
        // Reuse the existing Beta from PortfolioAnalyticsService and compute bench return over the same window.
        decimal alpha = 0m;
        try
        {
            var bars = await market.GetDailyBarsAsync(new[] { benchmark }, a.From, a.To, ct);
            if (bars.TryGetValue(benchmark, out var benchBars) && benchBars.Count >= 2)
            {
                var ord = benchBars.OrderBy(b => b.Date).ToList();
                var benchTotal = (double)((ord[^1].Close - ord[0].Close) / ord[0].Close);
                var portTotal = (double)a.Twrr / 100.0;
                alpha = (decimal)Math.Round(portTotal - (double)a.Beta * benchTotal, 4);
            }
        }
        catch { /* benchmark unavailable */ }

        return new RiskMetricsDto(
            portfolioId, a.From, a.To,
            Sharpe: a.Sharpe,
            Sortino: (decimal)Math.Round(sortino, 4),
            MaxDrawdown: a.MaxDrawdown,
            MaxDrawdownDate: a.MaxDrawdownDate,
            Var95: (decimal)Math.Round(var95 * 100, 4),
            Var99: (decimal)Math.Round(var99 * 100, 4),
            Cvar95: (decimal)Math.Round(cvar95 * 100, 4),
            AnnualisedVolatility: (decimal)Math.Round(annualVol * 100, 4),
            DownsideVolatility: (decimal)Math.Round(annualDownVol * 100, 4),
            Beta: a.Beta,
            Alpha: alpha,
            BenchmarkSymbol: benchmark);
    }
}

/// <summary>
/// M9 #103. Renders a parallel benchmark equity curve normalised to the
/// portfolio's starting value so both lines share an origin, plus
/// outperformance in basis points and alpha/beta. Supports either a single
/// benchmark ticker or a weighted blend.
/// </summary>
public sealed class BenchmarkComparisonService(
    StockyDbContext db,
    PortfolioHistoryService history,
    IMarketDataProvider market,
    IAdvancedMarketDataProvider advanced)
{
    public async Task<BenchmarkComparisonDto?> BuildAsync(
        Guid portfolioId,
        string ownerId,
        BenchmarkConfigDto? config,
        CancellationToken ct = default)
    {
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return null;
        var hist = await history.BuildAsync(portfolioId, ownerId, ct);
        if (hist is null || hist.Series.Count < 2) return null;

        var blend = config?.Blend is { Count: > 0 } b
            ? b
            : new List<BenchmarkComponentDto>
            {
                new(config?.Symbol ?? portfolio.BenchmarkSymbol ?? "SPY", 1m)
            };
        // Normalise weights so any non-1 sum still produces a valid blend.
        var totalWeight = blend.Sum(c => c.Weight);
        if (totalWeight <= 0) totalWeight = 1m;
        var normalised = blend.Select(c => new BenchmarkComponentDto(c.Symbol.ToUpperInvariant(), c.Weight / totalWeight)).ToList();
        var label = normalised.Count == 1
            ? normalised[0].Symbol
            : string.Join(" + ", normalised.Select(c => $"{c.Symbol} {Math.Round(c.Weight * 100, 0)}%"));

        var from = hist.Series[0].Date;
        var to = hist.Series[^1].Date;

        // Fetch each benchmark constituent's daily closes. Fall back to the
        // advanced provider's OHLC walk when GetDailyBarsAsync returns nothing
        // (e.g. the stub market provider).
        var closes = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>(StringComparer.OrdinalIgnoreCase);
        var symbols = normalised.Select(c => c.Symbol).Distinct().ToList();
        IReadOnlyDictionary<string, IReadOnlyList<DailyBarDto>> bars =
            new Dictionary<string, IReadOnlyList<DailyBarDto>>();
        try
        {
            bars = await market.GetDailyBarsAsync(symbols, from, to, ct);
        }
        catch { /* leave bars empty */ }
        foreach (var sym in symbols)
        {
            if (bars.TryGetValue(sym, out var list) && list.Count >= 2)
            {
                closes[sym] = list.ToDictionary(b => b.Date, b => b.Close);
            }
            else
            {
                var ohlc = await advanced.GetOhlcAsync(sym, from, to, ct);
                closes[sym] = ohlc.ToDictionary(b => b.Date, b => b.Close);
            }
        }

        // Per-day blended benchmark price (carry-forward when missing).
        decimal? startBlend = null;
        var lastBySym = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var startPortfolio = hist.Series[0].TotalEquity;
        if (startPortfolio <= 0) startPortfolio = 1m;

        var points = new List<BenchmarkPointDto>(hist.Series.Count);
        decimal endPortfolioReturn = 0;
        decimal endBenchReturn = 0;

        foreach (var p in hist.Series)
        {
            decimal blendPrice = 0m;
            foreach (var c in normalised)
            {
                if (closes.TryGetValue(c.Symbol, out var map) && map.TryGetValue(p.Date, out var px))
                {
                    lastBySym[c.Symbol] = px;
                }
                if (lastBySym.TryGetValue(c.Symbol, out var lastPx))
                {
                    blendPrice += c.Weight * lastPx;
                }
            }
            if (blendPrice <= 0) blendPrice = startBlend ?? 1m;
            startBlend ??= blendPrice;
            var benchEquity = startPortfolio * (blendPrice / startBlend!.Value);
            var portRet = (p.TotalEquity - startPortfolio) / startPortfolio;
            var benchRet = (benchEquity - startPortfolio) / startPortfolio;
            var bps = Math.Round((portRet - benchRet) * 10_000m, 2);
            points.Add(new BenchmarkPointDto(p.Date, p.TotalEquity, Math.Round(benchEquity, 2), bps));
            endPortfolioReturn = portRet;
            endBenchReturn = benchRet;
        }

        // Beta / Alpha vs the blended bench using daily simple returns.
        var pReturns = new List<double>();
        var bReturns = new List<double>();
        for (var i = 1; i < points.Count; i++)
        {
            var prevP = points[i - 1].PortfolioEquity;
            var prevB = points[i - 1].BenchmarkEquity;
            if (prevP <= 0 || prevB <= 0) continue;
            pReturns.Add((double)((points[i].PortfolioEquity - prevP) / prevP));
            bReturns.Add((double)((points[i].BenchmarkEquity - prevB) / prevB));
        }
        double beta = 0, alpha = 0;
        if (pReturns.Count >= 2)
        {
            var pMean = pReturns.Average();
            var bMean = bReturns.Average();
            double cov = 0, bVar = 0;
            for (var i = 0; i < pReturns.Count; i++)
            {
                cov += (pReturns[i] - pMean) * (bReturns[i] - bMean);
                bVar += (bReturns[i] - bMean) * (bReturns[i] - bMean);
            }
            if (bVar > 0) beta = cov / bVar;
            alpha = (double)endPortfolioReturn - beta * (double)endBenchReturn;
        }

        return new BenchmarkComparisonDto(
            portfolioId,
            label,
            from,
            to,
            Math.Round(endPortfolioReturn * 100m, 4),
            Math.Round(endBenchReturn * 100m, 4),
            Math.Round((endPortfolioReturn - endBenchReturn) * 10_000m, 2),
            (decimal)Math.Round(alpha * 100, 4),
            (decimal)Math.Round(beta, 4),
            points);
    }
}

/// <summary>
/// M9 #24. Historical backtesting engine — given a starting cash balance,
/// a monthly contribution, a rebalance frequency, and a set of target weights,
/// walks the OHLC history of every target symbol day-by-day, applies
/// contributions on the first weekday of the cadence period, and rebalances to
/// target weights on those same dates. Reports a benchmark line for comparison.
/// </summary>
public sealed class BacktestService(
    StockyDbContext db,
    IAdvancedMarketDataProvider advanced)
{
    public async Task<BacktestDto?> RunAsync(string ownerId, BacktestRequest req, CancellationToken ct = default)
    {
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == req.PortfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return null;
        if (req.Targets.Count == 0) return null;
        var totalWeight = req.Targets.Sum(t => t.TargetWeightPercent);
        if (totalWeight <= 0) return null;
        var from = req.From;
        var to = req.To;
        if (to < from) (from, to) = (to, from);
        // Cap span at 30 years to bound work.
        if (to.DayNumber - from.DayNumber > 365 * 30) to = from.AddDays(365 * 30);

        var benchmark = string.IsNullOrWhiteSpace(portfolio.BenchmarkSymbol) ? "SPY" : portfolio.BenchmarkSymbol!;

        var symbols = req.Targets.Select(t => t.Symbol.ToUpperInvariant()).Distinct().ToList();
        // Fetch each target's bars + benchmark bars in parallel.
        var fetches = symbols.Append(benchmark).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(async s => (Symbol: s, Bars: await advanced.GetOhlcAsync(s, from, to, ct)));
        var fetched = await Task.WhenAll(fetches);
        var priceMap = fetched.ToDictionary(
            x => x.Symbol,
            x => x.Bars.ToDictionary(b => b.Date, b => b.Close),
            StringComparer.OrdinalIgnoreCase);

        var tradingDays = priceMap.Values
            .SelectMany(m => m.Keys)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        if (tradingDays.Count == 0) return null;

        // Per-symbol share holdings + cash.
        var shares = symbols.ToDictionary(s => s, _ => 0m, StringComparer.OrdinalIgnoreCase);
        var cash = req.InitialCash;
        var contributions = req.InitialCash;
        var benchShares = 0m;
        if (priceMap.TryGetValue(benchmark, out var bench0) && bench0.Count > 0)
        {
            var first = bench0.OrderBy(kv => kv.Key).First();
            if (first.Value > 0) benchShares = req.InitialCash / first.Value;
        }
        var benchContrib = req.InitialCash;

        var freqMonths = req.Frequency?.ToLowerInvariant() switch
        {
            "quarterly" => 3,
            "yearly" => 12,
            _ => 1
        };
        var nextContrib = NextRebalance(from, freqMonths);

        var series = new List<BacktestPointDto>(tradingDays.Count);
        decimal peak = 0, maxDd = 0;

        decimal LastPrice(string sym, DateOnly date)
        {
            if (!priceMap.TryGetValue(sym, out var m)) return 0;
            if (m.TryGetValue(date, out var px)) return px;
            // Walk back up to 5 trading days to find the previous close.
            for (var i = 1; i <= 5; i++)
            {
                if (m.TryGetValue(date.AddDays(-i), out var prev)) return prev;
            }
            return 0;
        }

        decimal PortfolioValue(DateOnly date)
        {
            var val = cash;
            foreach (var (sym, qty) in shares)
            {
                if (qty > 0) val += qty * LastPrice(sym, date);
            }
            return val;
        }

        for (int i = 0; i < tradingDays.Count; i++)
        {
            var day = tradingDays[i];
            // Apply contribution and rebalance on the first trading day on/after nextContrib.
            if (day >= nextContrib)
            {
                if (i > 0)
                {
                    cash += req.MonthlyContribution * freqMonths;
                    contributions += req.MonthlyContribution * freqMonths;
                    // Also add to benchmark.
                    var bpx = LastPrice(benchmark, day);
                    if (bpx > 0) benchShares += (req.MonthlyContribution * freqMonths) / bpx;
                    benchContrib += req.MonthlyContribution * freqMonths;
                }
                // Rebalance to targets.
                var total = PortfolioValue(day);
                if (total > 0)
                {
                    foreach (var t in req.Targets)
                    {
                        var sym = t.Symbol.ToUpperInvariant();
                        var target = total * (t.TargetWeightPercent / totalWeight);
                        var px = LastPrice(sym, day);
                        if (px <= 0) continue;
                        shares[sym] = Math.Round(target / px, 6);
                    }
                    // Cash = whatever isn't allocated.
                    var allocated = 0m;
                    foreach (var (sym, qty) in shares)
                    {
                        allocated += qty * LastPrice(sym, day);
                    }
                    cash = Math.Max(0, total - allocated);
                }
                nextContrib = NextRebalance(day.AddDays(1), freqMonths);
            }

            var equity = PortfolioValue(day);
            var benchEq = benchShares > 0 ? benchShares * LastPrice(benchmark, day) : 0m;
            series.Add(new BacktestPointDto(day, Math.Round(equity, 2), Math.Round(contributions, 2), Math.Round(benchEq, 2)));
            if (equity > peak) peak = equity;
            if (peak > 0)
            {
                var dd = (equity - peak) / peak * 100m;
                if (dd < maxDd) maxDd = dd;
            }
        }

        var finalEquity = series[^1].Equity;
        var totalRet = req.InitialCash > 0 && contributions > 0
            ? (finalEquity - contributions) / contributions * 100m
            : 0m;
        var benchFinal = series[^1].BenchmarkEquity;
        var benchRet = benchContrib > 0 ? (benchFinal - benchContrib) / benchContrib * 100m : 0m;

        var years = (to.DayNumber - from.DayNumber) / 365.25;
        decimal cagr = 0, benchCagr = 0;
        if (years > 0 && contributions > 0 && finalEquity > 0)
        {
            cagr = (decimal)((Math.Pow((double)(finalEquity / contributions), 1.0 / years) - 1) * 100);
        }
        if (years > 0 && benchContrib > 0 && benchFinal > 0)
        {
            benchCagr = (decimal)((Math.Pow((double)(benchFinal / benchContrib), 1.0 / years) - 1) * 100);
        }

        return new BacktestDto(
            req.PortfolioId,
            benchmark,
            finalEquity,
            Math.Round(contributions, 2),
            Math.Round(totalRet, 4),
            Math.Round(cagr, 4),
            Math.Round(maxDd, 4),
            benchFinal,
            Math.Round(benchRet, 4),
            Math.Round(benchCagr, 4),
            series);
    }

    private static DateOnly NextRebalance(DateOnly d, int months)
    {
        var dt = new DateTime(d.Year, d.Month, 1).AddMonths(months);
        return DateOnly.FromDateTime(dt);
    }
}

/// <summary>
/// M9 #104. Goal projection — compounds <c>currentValue</c> with monthly
/// contributions at <c>expectedReturn</c> (annualised) and reports current
/// progress, projected hit date, and a monthly projection trajectory.
/// </summary>
public sealed class GoalsService(StockyDbContext db)
{
    public async Task<IReadOnlyList<GoalDto>> ListAsync(string ownerId, CancellationToken ct = default)
    {
        var goals = await db.Goals.Where(g => g.OwnerId == ownerId).ToListAsync(ct);
        if (goals.Count == 0) return Array.Empty<GoalDto>();
        var portfolioIds = goals.Where(g => g.PortfolioId.HasValue).Select(g => g.PortfolioId!.Value).Distinct().ToList();
        var snapshots = portfolioIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await db.PortfolioSnapshots
                .Where(s => portfolioIds.Contains(s.PortfolioId))
                .GroupBy(s => s.PortfolioId)
                .Select(g => new { g.Key, Latest = g.OrderByDescending(x => x.Date).First().MarketValue })
                .ToDictionaryAsync(x => x.Key, x => x.Latest, ct);

        var holdings = portfolioIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await db.Holdings
                .Where(h => portfolioIds.Contains(h.PortfolioId))
                .GroupBy(h => h.PortfolioId)
                .Select(g => new { g.Key, Total = g.Sum(h => h.Quantity * h.AverageCost) })
                .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

        return goals.Select(g =>
        {
            decimal current = 0m;
            if (g.PortfolioId.HasValue)
            {
                if (!snapshots.TryGetValue(g.PortfolioId.Value, out current))
                {
                    holdings.TryGetValue(g.PortfolioId.Value, out current);
                }
            }
            return Project(g, current);
        }).ToList();
    }

    public async Task<GoalDto?> GetAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == ownerId, ct);
        if (goal is null) return null;
        decimal current = 0m;
        if (goal.PortfolioId.HasValue)
        {
            var snap = await db.PortfolioSnapshots
                .Where(s => s.PortfolioId == goal.PortfolioId.Value)
                .OrderByDescending(s => s.Date)
                .FirstOrDefaultAsync(ct);
            if (snap != null) current = snap.MarketValue;
            else
            {
                current = await db.Holdings
                    .Where(h => h.PortfolioId == goal.PortfolioId.Value)
                    .SumAsync(h => h.Quantity * h.AverageCost, ct);
            }
        }
        return Project(goal, current);
    }

    public async Task<GoalDto> CreateAsync(string ownerId, GoalCreateDto dto, CancellationToken ct = default)
    {
        var goal = new Goal
        {
            OwnerId = ownerId,
            PortfolioId = dto.PortfolioId,
            Name = dto.Name.Trim(),
            TargetValue = dto.TargetValue,
            TargetDate = dto.TargetDate,
            MonthlyContribution = dto.MonthlyContribution,
            ExpectedReturn = dto.ExpectedReturn
        };
        db.Goals.Add(goal);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(ownerId, goal.Id, ct))!;
    }

    public async Task<GoalDto?> UpdateAsync(string ownerId, Guid id, GoalCreateDto dto, CancellationToken ct = default)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == ownerId, ct);
        if (goal is null) return null;
        goal.PortfolioId = dto.PortfolioId;
        goal.Name = dto.Name.Trim();
        goal.TargetValue = dto.TargetValue;
        goal.TargetDate = dto.TargetDate;
        goal.MonthlyContribution = dto.MonthlyContribution;
        goal.ExpectedReturn = dto.ExpectedReturn;
        await db.SaveChangesAsync(ct);
        return await GetAsync(ownerId, id, ct);
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var goal = await db.Goals.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == ownerId, ct);
        if (goal is null) return false;
        db.Goals.Remove(goal);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static GoalDto Project(Goal g, decimal currentValue)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var months = MonthsBetween(today, g.TargetDate);
        if (months < 0) months = 0;
        var monthlyRate = (double)g.ExpectedReturn / 12.0;

        decimal value = currentValue;
        var projection = new List<GoalProjectionPointDto>(months + 1);
        // Linear trajectory from currentValue → targetValue used as the
        // "on-track" baseline so the UI can show whether projected is above
        // or below that line.
        var trajectoryStep = months > 0 ? (g.TargetValue - currentValue) / months : 0m;
        DateOnly? hitDate = null;
        var month = today;
        for (int i = 0; i <= months; i++)
        {
            projection.Add(new GoalProjectionPointDto(month,
                Math.Round(value, 2),
                Math.Round(currentValue + trajectoryStep * i, 2)));
            if (hitDate is null && value >= g.TargetValue && g.TargetValue > 0) hitDate = month;
            // compound + contribute for next step
            value = value * (decimal)(1 + monthlyRate) + g.MonthlyContribution;
            month = month.AddMonths(1);
        }

        var finalValue = projection.Count > 0 ? projection[^1].ProjectedValue : currentValue;
        var progress = g.TargetValue > 0 ? Math.Round(currentValue / g.TargetValue * 100m, 2) : 0m;
        var onTrack = finalValue >= g.TargetValue;

        return new GoalDto(
            g.Id,
            g.PortfolioId,
            g.Name,
            g.TargetValue,
            g.TargetDate,
            g.MonthlyContribution,
            g.ExpectedReturn,
            Math.Round(currentValue, 2),
            progress,
            hitDate,
            onTrack,
            finalValue,
            projection);
    }

    private static int MonthsBetween(DateOnly a, DateOnly b)
        => (b.Year - a.Year) * 12 + (b.Month - a.Month);
}

/// <summary>
/// M9 #95. Earnings surprise history (last 8 quarters by default) for a
/// single symbol — deterministic synthesis on top of the existing earnings
/// provider so the UI can render the drilldown chart without a real feed.
/// </summary>
public sealed class EarningsSurpriseService
{
    public IReadOnlyList<EarningsSurprisePointDto> Build(string symbol, int quarters = 8)
    {
        symbol = (symbol ?? string.Empty).ToUpperInvariant();
        if (quarters < 1) quarters = 1;
        if (quarters > 24) quarters = 24;
        var rng = new Random(Hash(symbol));
        var list = new List<EarningsSurprisePointDto>(quarters);
        // Walk back quarter-by-quarter from the most recent past quarter end.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var qEnd = QuarterEnd(today.AddMonths(-3));
        for (int i = 0; i < quarters; i++)
        {
            var estimate = Math.Round(0.5m + (decimal)rng.NextDouble() * 4m, 2);
            var surprise = (decimal)((rng.NextDouble() - 0.4) * 0.2);
            var actual = Math.Round(estimate * (1 + surprise), 2);
            list.Add(new EarningsSurprisePointDto(qEnd, estimate, actual,
                estimate == 0 ? 0 : Math.Round(surprise * 100m, 2)));
            qEnd = QuarterEnd(qEnd.AddMonths(-3));
        }
        return list.OrderBy(p => p.Date).ToList();
    }

    private static DateOnly QuarterEnd(DateOnly d)
    {
        var q = ((d.Month - 1) / 3) + 1;
        var month = q * 3;
        var day = DateTime.DaysInMonth(d.Year, month);
        return new DateOnly(d.Year, month, day);
    }

    private static int Hash(string s)
    {
        var h = 0;
        foreach (var c in s) h = unchecked(h * 31 + c);
        return Math.Abs(h);
    }
}

using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Advanced risk analytics:
///   - Enhanced VaR (Historical + Parametric + Monte Carlo).  GitHub issue 2.1.
///   - Stress testing with preset and custom factor shocks.    GitHub issue 2.2.
///   - Liquidity risk (days-to-liquidate vs ADV).              GitHub issue 2.4.
///   - Concentration risk (HHI + sector/country buckets).      GitHub issue 2.5.
/// Pure math against existing market data and holdings — no new external feeds.
/// </summary>
public sealed class AdvancedRiskService(
    StockyDbContext db,
    PortfolioAnalyticsService analytics,
    PortfolioHistoryService history,
    IAdvancedMarketDataProvider advanced)
{
    // ---------- 2.1  VaR (3 methods + CVaR) ---------------------------------
    public async Task<VarSuiteDto?> BuildVarAsync(
        Guid portfolioId,
        string ownerId,
        decimal confidence,
        int holdingDays,
        int monteCarloSims,
        CancellationToken ct = default)
    {
        if (confidence <= 0 || confidence >= 1) confidence = 0.95m;
        if (holdingDays < 1) holdingDays = 1;
        if (monteCarloSims < 1000) monteCarloSims = 10_000;

        var a = await analytics.BuildAsync(portfolioId, ownerId, ct);
        if (a is null) return null;
        var portfolioValue = a.PeakEquity; // most recent peak; use latest snapshot value
        var hist = await history.BuildAsync(portfolioId, ownerId, ct);
        if (hist is not null && hist.Series.Count > 0)
            portfolioValue = hist.Series[^1].TotalEquity;

        var rets = a.DailyReturnSeries.Select(d => (double)d.ReturnPercent / 100.0).ToArray();
        if (rets.Length < 2)
            return new VarSuiteDto(portfolioId, a.From, a.To, confidence, holdingDays, portfolioValue,
                0, 0, 0, 0, 0, 0, 0, monteCarloSims, Array.Empty<WorstScenarioDto>());

        var sqrtT = Math.Sqrt(holdingDays);
        var p = 1.0 - (double)confidence;

        // -- Historical VaR (one-day) -------------------------------
        var sorted = rets.OrderBy(r => r).ToArray();
        var idx = (int)Math.Floor(p * sorted.Length);
        idx = Math.Clamp(idx, 0, sorted.Length - 1);
        var oneDayVarHist = -sorted[idx];                 // positive loss magnitude
        var tail = sorted.Take(Math.Max(1, idx)).ToArray();
        var oneDayCvarHist = tail.Length == 0 ? 0 : -tail.Average();

        // -- Parametric (normal approx) ------------------------------
        var mean = rets.Average();
        var variance = rets.Sum(r => (r - mean) * (r - mean)) / (rets.Length - 1);
        var stdev = Math.Sqrt(variance);
        var z = InverseStandardNormal(p);                 // negative for p<0.5
        var oneDayVarParam = -(mean + z * stdev);

        // -- Monte Carlo (Box-Muller, normal returns calibrated to sample) --
        var rng = new Random(unchecked(portfolioId.GetHashCode() ^ rets.Length));
        var simReturns = new double[monteCarloSims];
        for (int i = 0; i < monteCarloSims; i++)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            double zN = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            simReturns[i] = mean + stdev * zN;
        }
        Array.Sort(simReturns);
        var mcIdx = Math.Clamp((int)Math.Floor(p * monteCarloSims), 0, monteCarloSims - 1);
        var oneDayVarMc = -simReturns[mcIdx];

        // Scale 1-day to N-day using sqrt(T) (standard regulatory convention).
        decimal Pct(double v) => (decimal)Math.Round(Math.Max(0, v) * sqrtT * 100.0, 4);
        decimal Dollars(double v) => Math.Round(portfolioValue * (decimal)(Math.Max(0, v) * sqrtT), 2);

        var worst = a.DailyReturnSeries
            .OrderBy(d => d.ReturnPercent)
            .Take(10)
            .Select(d => new WorstScenarioDto(
                d.Date,
                d.ReturnPercent,
                Math.Round(portfolioValue * (d.ReturnPercent / 100m), 2)))
            .ToList();

        return new VarSuiteDto(
            portfolioId, a.From, a.To, confidence, holdingDays, portfolioValue,
            VarHistoricalDollars: Dollars(oneDayVarHist),
            VarParametricDollars: Dollars(oneDayVarParam),
            VarMonteCarloDollars: Dollars(oneDayVarMc),
            CvarHistoricalDollars: Dollars(oneDayCvarHist),
            VarHistoricalPercent: Pct(oneDayVarHist),
            VarParametricPercent: Pct(oneDayVarParam),
            VarMonteCarloPercent: Pct(oneDayVarMc),
            MonteCarloSimulations: monteCarloSims,
            WorstScenarios: worst);
    }

    /// <summary>
    /// Beasley-Springer-Moro rational approximation of the inverse standard
    /// normal CDF — accurate to ~7 decimal places in the tails. Avoids
    /// pulling in MathNet just for one call.
    /// </summary>
    internal static double InverseStandardNormal(double p)
    {
        if (p <= 0) return double.NegativeInfinity;
        if (p >= 1) return double.PositiveInfinity;
        // Beasley-Springer / Moro
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02,
                        1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02,
                        6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00,
                       -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00,
                       3.754408661907416e+00 };
        double plow = 0.02425, phigh = 1 - plow;
        double q, r;
        if (p < plow)
        {
            q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                   ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p <= phigh)
        {
            q = p - 0.5; r = q * q;
            return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
                   (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        q = Math.Sqrt(-2 * Math.Log(1 - p));
        return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
    }

    // ---------- 2.2  Stress Testing -----------------------------------------
    public static readonly IReadOnlyList<StressScenarioDto> PresetScenarios = new List<StressScenarioDto>
    {
        new("gfc_2008", "2008 Global Financial Crisis",
            "Lehman collapse window. S&P -38%, Fed cuts 150bps, USD +12%, oil -55%, VIX spike.",
            new StressShockDto(-0.38m, -0.015m, 0.12m, -0.55m, 2.00m)),
        new("covid_2020", "2020 COVID Crash",
            "Feb-Mar 2020 pandemic shock. S&P -34%, Fed cuts 120bps, oil -65%.",
            new StressShockDto(-0.34m, -0.012m, 0.05m, -0.65m, 3.00m)),
        new("rate_shock_2022", "2022 Rate Shock",
            "Fed hiking cycle. S&P -19%, +430bp on the long end, USD +15%, oil +40%.",
            new StressShockDto(-0.19m, 0.043m, 0.15m, 0.40m, 0.50m)),
        new("black_monday_1987", "1987 Black Monday",
            "Single-day -22% equity crash with limited cross-asset impact.",
            new StressShockDto(-0.22m, 0.0m, 0.0m, 0.0m, 0.0m)),
    };

    public async Task<StressTestResultDto?> RunStressAsync(
        StressTestRequest req,
        string ownerId,
        CancellationToken ct = default)
    {
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == req.PortfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return null;

        StressShockDto shock;
        string scenarioId, scenarioName;
        if (!string.IsNullOrWhiteSpace(req.ScenarioId))
        {
            var preset = PresetScenarios.FirstOrDefault(s => s.Id == req.ScenarioId);
            if (preset is null) return null;
            shock = preset.Shock; scenarioId = preset.Id; scenarioName = preset.Name;
        }
        else
        {
            shock = req.CustomShock ?? new StressShockDto(0, 0, 0, 0, 0);
            scenarioId = "custom"; scenarioName = "Custom shock";
        }

        var holdings = await db.Holdings.Where(h => h.PortfolioId == req.PortfolioId).ToListAsync(ct);
        var symbols = holdings.Select(h => h.Symbol).ToList();
        var quotes = await db.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToDictionaryAsync(q => q.Symbol, q => q.Price, ct);
        var meta = await db.InstrumentMetadata
            .Where(m => symbols.Contains(m.Symbol))
            .ToDictionaryAsync(m => m.Symbol, ct);

        var impacts = new List<StressHoldingImpactDto>(holdings.Count);
        decimal totalPnl = 0m;
        decimal totalValue = 0m;
        foreach (var h in holdings)
        {
            quotes.TryGetValue(h.Symbol, out var price);
            var mv = price * h.Quantity;
            totalValue += mv;
            var beta = meta.TryGetValue(h.Symbol, out var m) && m.Beta.HasValue ? m.Beta.Value : 1.0m;
            // Equity shock dominant; small drift from oil and rates (light heuristic, not factor model)
            var equityImpact = beta * shock.EquityShock;
            var oilImpact = 0.05m * shock.OilShock;          // small commodity beta
            var ratesImpact = -3.0m * shock.RatesShock;       // duration ~3 yrs equivalent for equities
            var pctImpact = equityImpact + oilImpact + ratesImpact;
            var pnl = Math.Round(mv * pctImpact, 2);
            totalPnl += pnl;
            impacts.Add(new StressHoldingImpactDto(
                h.Symbol, h.Quantity, Math.Round(mv, 2),
                Math.Round(beta, 4), pnl, Math.Round(pctImpact * 100, 4)));
        }

        var pctTotal = totalValue == 0 ? 0m : Math.Round(totalPnl / totalValue * 100m, 4);
        return new StressTestResultDto(
            req.PortfolioId, scenarioId, scenarioName, shock,
            Math.Round(totalValue, 2), Math.Round(totalPnl, 2), pctTotal, impacts);
    }

    // ---------- 2.4  Liquidity Risk -----------------------------------------
    public async Task<LiquidityRiskDto?> BuildLiquidityAsync(
        Guid portfolioId,
        string ownerId,
        decimal maxParticipation,
        int thresholdDays,
        int advLookback,
        CancellationToken ct = default)
    {
        if (maxParticipation <= 0 || maxParticipation > 1) maxParticipation = 0.20m;
        if (thresholdDays <= 0) thresholdDays = 5;
        if (advLookback < 5) advLookback = 30;

        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return null;
        var holdings = await db.Holdings.Where(h => h.PortfolioId == portfolioId).ToListAsync(ct);

        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-(advLookback + 10)); // buffer for weekends/holidays

        var positions = new List<LiquidityPositionDto>(holdings.Count);
        decimal valueWeightedDays = 0;
        decimal totalValue = 0;
        foreach (var h in holdings)
        {
            var bars = await advanced.GetOhlcAsync(h.Symbol, from, to, ct);
            // Volume in OhlcBarDto is total shares per day.
            var recent = bars.OrderByDescending(b => b.Date).Take(advLookback).ToList();
            decimal advShares = recent.Count == 0 ? 0 : (decimal)recent.Average(b => (double)b.Volume);
            decimal lastClose = recent.Count == 0 ? 0 : recent[0].Close;
            var marketValue = lastClose * h.Quantity;
            totalValue += marketValue;
            var advDollars = lastClose * advShares;
            decimal days = advShares <= 0
                ? decimal.MaxValue
                : Math.Round(h.Quantity / (advShares * maxParticipation), 4);
            var illiquid = days > thresholdDays;
            positions.Add(new LiquidityPositionDto(
                h.Symbol, h.Quantity, Math.Round(marketValue, 2),
                Math.Round(advShares, 0), Math.Round(advDollars, 2),
                days == decimal.MaxValue ? 9999m : days,
                illiquid));
            if (marketValue > 0 && days != decimal.MaxValue)
                valueWeightedDays += marketValue * days;
        }

        var score = totalValue > 0 ? Math.Round(valueWeightedDays / totalValue, 4) : 0m;
        return new LiquidityRiskDto(
            portfolioId, maxParticipation, thresholdDays, advLookback,
            score, positions);
    }

    // ---------- 2.5  Concentration / HHI ------------------------------------
    public async Task<ConcentrationRiskDto?> BuildConcentrationAsync(
        Guid portfolioId,
        string ownerId,
        decimal maxPositionWeight,
        decimal maxSectorWeight,
        decimal maxCountryWeight,
        CancellationToken ct = default)
    {
        if (maxPositionWeight <= 0) maxPositionWeight = 0.10m;
        if (maxSectorWeight <= 0) maxSectorWeight = 0.30m;
        if (maxCountryWeight <= 0) maxCountryWeight = 0.40m;

        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (portfolio is null) return null;
        var holdings = await db.Holdings.Where(h => h.PortfolioId == portfolioId).ToListAsync(ct);
        var symbols = holdings.Select(h => h.Symbol).ToList();
        var quotes = await db.PriceQuotes
            .Where(q => symbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(x => x.AsOf).First())
            .ToDictionaryAsync(q => q.Symbol, q => q.Price, ct);
        var meta = await db.InstrumentMetadata
            .Where(m => symbols.Contains(m.Symbol))
            .ToDictionaryAsync(m => m.Symbol, ct);

        var values = holdings
            .Select(h => new
            {
                h.Symbol,
                Value = (quotes.TryGetValue(h.Symbol, out var p) ? p : 0m) * h.Quantity,
                Sector = meta.TryGetValue(h.Symbol, out var m) ? m.Sector ?? "Unknown" : "Unknown",
                Country = meta.TryGetValue(h.Symbol, out var m2) ? m2.Country ?? "Unknown" : "Unknown",
            })
            .ToList();
        var total = values.Sum(v => v.Value);
        if (total <= 0)
            return new ConcentrationRiskDto(portfolioId, 0, "empty", 100m,
                Array.Empty<ConcentrationBucketDto>(),
                Array.Empty<ConcentrationBucketDto>(),
                Array.Empty<ConcentrationBucketDto>(),
                Array.Empty<ConcentrationBreachDto>());

        IReadOnlyList<ConcentrationBucketDto> Bucket(IEnumerable<(string Key, decimal Value)> rows)
        {
            return rows
                .GroupBy(r => r.Key)
                .Select(g => new ConcentrationBucketDto(g.Key, Math.Round(g.Sum(x => x.Value) / total, 6)))
                .OrderByDescending(b => b.Weight)
                .ToList();
        }

        var positions = Bucket(values.Select(v => (v.Symbol, v.Value)));
        var sectors = Bucket(values.Select(v => (v.Sector, v.Value)));
        var countries = Bucket(values.Select(v => (v.Country, v.Value)));

        // HHI on position weights
        var hhi = positions.Sum(p => p.Weight * p.Weight) * 10000m;
        var interpretation = hhi < 1500m ? "diversified" : hhi <= 2500m ? "moderate" : "concentrated";

        // Diversification score: 100 = perfectly even spread across N positions (HHI = 10000/N),
        // 0 = entire portfolio in a single position (HHI = 10000).
        var n = positions.Count;
        var ideal = n > 0 ? 10000m / n : 10000m;
        var diversification = n <= 1 ? 0m : Math.Max(0m, Math.Round((1m - (hhi - ideal) / (10000m - ideal)) * 100m, 2));

        var breaches = new List<ConcentrationBreachDto>();
        foreach (var p in positions.Where(p => p.Weight > maxPositionWeight))
            breaches.Add(new ConcentrationBreachDto("position", p.Key, Math.Round(p.Weight * 100, 4), Math.Round(maxPositionWeight * 100, 4)));
        foreach (var s in sectors.Where(s => s.Weight > maxSectorWeight))
            breaches.Add(new ConcentrationBreachDto("sector", s.Key, Math.Round(s.Weight * 100, 4), Math.Round(maxSectorWeight * 100, 4)));
        foreach (var c in countries.Where(c => c.Weight > maxCountryWeight))
            breaches.Add(new ConcentrationBreachDto("country", c.Key, Math.Round(c.Weight * 100, 4), Math.Round(maxCountryWeight * 100, 4)));

        return new ConcentrationRiskDto(
            portfolioId,
            Math.Round(hhi, 2),
            interpretation,
            diversification,
            positions, sectors, countries, breaches);
    }
}

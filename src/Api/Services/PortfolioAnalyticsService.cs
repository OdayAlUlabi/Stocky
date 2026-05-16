using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// Computes proper portfolio performance analytics from the daily history
/// produced by <see cref="PortfolioHistoryService"/>:
///   - TWRR (time-weighted return) via daily sub-period chaining, neutralising deposits/withdrawals.
///   - MWRR / XIRR (money-weighted) via Newton-Raphson on the actual cash-flow stream.
///   - Annualised return, volatility (annualised stdev of daily returns), Sharpe (rf=0),
///     max drawdown (peak-to-trough on equity curve), best/worst day, total dividends.
/// All math is done on the canonical history series so figures stay consistent with the
/// equity curve and capital-flow views the user already sees.
/// </summary>
public sealed class PortfolioAnalyticsService(PortfolioHistoryService history)
{
    public async Task<PortfolioAnalyticsDto?> BuildAsync(Guid portfolioId, string ownerId, CancellationToken ct = default)
    {
        var hist = await history.BuildAsync(portfolioId, ownerId, ct);
        if (hist is null) return null;
        if (hist.Series.Count < 2)
        {
            return new PortfolioAnalyticsDto(
                portfolioId, hist.Currency, hist.From, hist.To,
                TotalReturnPercent: hist.TotalReturnPercent,
                Twrr: 0, TwrrAnnualised: 0, Mwrr: 0,
                Volatility: 0, Sharpe: 0,
                MaxDrawdown: 0, MaxDrawdownDate: hist.To, PeakEquity: hist.TotalEquity,
                BestDay: 0, BestDayDate: hist.To,
                WorstDay: 0, WorstDayDate: hist.To,
                TotalDividends: 0, TtmDividends: 0, DividendYield: 0,
                DrawdownSeries: Array.Empty<DrawdownPointDto>(),
                DailyReturnSeries: Array.Empty<DailyReturnPointDto>());
        }

        // Daily cash flow = day-over-day change in NetContributions. Positive = deposit, negative = withdrawal.
        var series = hist.Series;
        var dailyFactors = new List<double>(series.Count);
        var dailyReturns = new List<DailyReturnPointDto>(series.Count);
        decimal peak = series[0].TotalEquity;
        decimal maxDd = 0m;
        DateOnly maxDdDate = series[0].Date;
        decimal bestDay = 0m, worstDay = 0m;
        DateOnly bestDate = series[0].Date, worstDate = series[0].Date;
        var drawdowns = new List<DrawdownPointDto>(series.Count);

        for (int i = 0; i < series.Count; i++)
        {
            var p = series[i];
            if (p.TotalEquity > peak) peak = p.TotalEquity;
            var dd = peak == 0 ? 0m : (p.TotalEquity - peak) / peak * 100m;
            if (dd < maxDd) { maxDd = dd; maxDdDate = p.Date; }
            drawdowns.Add(new DrawdownPointDto(p.Date, Math.Round(dd, 4)));

            if (i == 0) continue;
            var prev = series[i - 1];
            var flow = p.NetContributions - prev.NetContributions;
            // Sub-period return: (V_end - cashFlow) / V_start
            if (prev.TotalEquity <= 0) continue;
            var endLessFlow = p.TotalEquity - flow;
            var factor = (double)(endLessFlow / prev.TotalEquity);
            if (double.IsNaN(factor) || double.IsInfinity(factor)) continue;
            dailyFactors.Add(factor);
            var pct = (factor - 1.0) * 100.0;
            var pctDec = (decimal)Math.Round(pct, 4);
            dailyReturns.Add(new DailyReturnPointDto(p.Date, pctDec));
            if (pctDec > bestDay) { bestDay = pctDec; bestDate = p.Date; }
            if (pctDec < worstDay) { worstDay = pctDec; worstDate = p.Date; }
        }

        // TWRR = product of daily factors - 1
        double cumulative = 1.0;
        foreach (var f in dailyFactors) cumulative *= f;
        var twrr = (cumulative - 1.0) * 100.0;

        // Volatility: stdev of daily returns * sqrt(252) (annualised), in percent
        double vol = 0;
        double sharpe = 0;
        if (dailyFactors.Count >= 2)
        {
            var ret = dailyFactors.Select(f => f - 1.0).ToArray();
            var mean = ret.Average();
            var variance = ret.Sum(r => (r - mean) * (r - mean)) / (ret.Length - 1);
            var stdev = Math.Sqrt(variance);
            vol = stdev * Math.Sqrt(252) * 100.0;
            sharpe = stdev > 0 ? (mean * 252) / (stdev * Math.Sqrt(252)) : 0;
        }

        // Annualised TWRR
        var spanDays = (series[^1].Date.DayNumber - series[0].Date.DayNumber);
        double annualisedTwrr = 0;
        if (spanDays > 0 && cumulative > 0)
        {
            annualisedTwrr = (Math.Pow(cumulative, 365.0 / spanDays) - 1.0) * 100.0;
        }

        // MWRR / XIRR
        var cashFlows = BuildCashFlows(hist);
        var mwrr = ComputeXirr(cashFlows);

        // Dividends
        var dividends = hist.Events.Where(e => e.Type == "Dividend").ToList();
        var totalDivs = dividends.Sum(d => d.Amount);
        var ttmCutoff = series[^1].Date.AddDays(-365);
        var ttmDivs = dividends.Where(d => d.Date >= ttmCutoff).Sum(d => d.Amount);
        var yieldPct = hist.TotalEquity > 0 ? (ttmDivs / hist.TotalEquity) * 100m : 0m;

        return new PortfolioAnalyticsDto(
            portfolioId, hist.Currency, hist.From, hist.To,
            TotalReturnPercent: hist.TotalReturnPercent,
            Twrr: (decimal)Math.Round(twrr, 4),
            TwrrAnnualised: (decimal)Math.Round(annualisedTwrr, 4),
            Mwrr: (decimal)Math.Round(mwrr * 100.0, 4),
            Volatility: (decimal)Math.Round(vol, 4),
            Sharpe: (decimal)Math.Round(sharpe, 4),
            MaxDrawdown: Math.Round(maxDd, 4),
            MaxDrawdownDate: maxDdDate,
            PeakEquity: peak,
            BestDay: bestDay,
            BestDayDate: bestDate,
            WorstDay: worstDay,
            WorstDayDate: worstDate,
            TotalDividends: Math.Round(totalDivs, 2),
            TtmDividends: Math.Round(ttmDivs, 2),
            DividendYield: Math.Round(yieldPct, 4),
            DrawdownSeries: drawdowns,
            DailyReturnSeries: dailyReturns);
    }

    /// <summary>
    /// External cash flow list for XIRR: deposits = negative (money in), withdrawals = positive (money out),
    /// final terminal equity = positive (money returned). Dates are UTC days from the events list.
    /// </summary>
    private static List<(DateOnly Date, double Amount)> BuildCashFlows(PortfolioHistoryDto hist)
    {
        var flows = new List<(DateOnly, double)>();
        foreach (var ev in hist.Events)
        {
            switch (ev.Type)
            {
                case "Deposit":
                    flows.Add((ev.Date, -(double)ev.Amount));
                    break;
                case "Withdrawal":
                    flows.Add((ev.Date, -(double)ev.Amount)); // ev.Amount already negative
                    break;
            }
        }
        if (hist.TotalEquity != 0)
        {
            flows.Add((hist.To, (double)hist.TotalEquity));
        }
        return flows;
    }

    /// <summary>
    /// Newton-Raphson XIRR. Returns annualised rate (e.g. 0.12 = +12%/yr). 0 if it does not converge.
    /// </summary>
    private static double ComputeXirr(List<(DateOnly Date, double Amount)> flows)
    {
        if (flows.Count < 2) return 0;
        var d0 = flows[0].Date;
        var ts = flows.Select(f => (f.Date.DayNumber - d0.DayNumber) / 365.0).ToArray();
        var amts = flows.Select(f => f.Amount).ToArray();
        // need at least one positive and one negative
        if (!amts.Any(a => a > 0) || !amts.Any(a => a < 0)) return 0;

        double r = 0.1;
        for (int iter = 0; iter < 100; iter++)
        {
            double npv = 0, dnpv = 0;
            for (int i = 0; i < amts.Length; i++)
            {
                var denom = Math.Pow(1 + r, ts[i]);
                npv += amts[i] / denom;
                dnpv += -ts[i] * amts[i] / (denom * (1 + r));
            }
            if (Math.Abs(dnpv) < 1e-12) break;
            var step = npv / dnpv;
            var nr = r - step;
            if (nr < -0.999) nr = -0.999; // keep (1+r) positive
            if (Math.Abs(nr - r) < 1e-7) { r = nr; break; }
            r = nr;
        }
        if (double.IsNaN(r) || double.IsInfinity(r)) return 0;
        return r;
    }
}

namespace Stocky.Api.Services;

/// <summary>
/// Time-Weighted Return (TWRR) and Money-Weighted Return (MWRR / XIRR) over a
/// portfolio's value series and external cash-flow series. Both returns are
/// annualised when <c>annualise = true</c>; otherwise raw period returns.
/// </summary>
/// <remarks>
/// External flow convention (portfolio perspective):
///   +flow = contribution into the portfolio (e.g. BUY cost)
///   -flow = withdrawal from the portfolio (e.g. SELL proceeds)
/// For MWRR we flip sign because XIRR is computed from the investor's wallet.
/// </remarks>
public static class ReturnsCalculator
{
    /// <summary>
    /// Chain-linked Time-Weighted Rate of Return. For each day after the first
    /// snapshot: r_i = (V_i - V_{i-1} - flow_i) / V_{i-1}; TWRR = prod(1+r) - 1.
    /// Days where V_{i-1} ≤ 0 are skipped (the period has no investment base).
    /// </summary>
    public static decimal Twrr(IReadOnlyList<(DateOnly Date, decimal Value, decimal ExternalFlow)> points)
    {
        if (points.Count < 2) return 0m;
        decimal product = 1m;
        for (var i = 1; i < points.Count; i++)
        {
            var prev = points[i - 1].Value;
            if (prev <= 0m) continue;
            var r = (points[i].Value - prev - points[i].ExternalFlow) / prev;
            product *= 1m + r;
        }
        return product - 1m;
    }

    /// <summary>
    /// Money-Weighted Rate of Return (XIRR). Solves for annualised rate r such
    /// that sum(cf_i / (1+r)^((d_i - d_0)/365)) = 0. Newton-Raphson with a few
    /// guess seeds; returns 0 if no convergence or all-same-sign flows.
    /// </summary>
    public static decimal Mwrr(IReadOnlyList<(DateOnly Date, decimal Amount)> cashflows)
    {
        if (cashflows.Count < 2) return 0m;
        // Need at least one positive and one negative flow for IRR to exist.
        var hasPos = false; var hasNeg = false;
        foreach (var (_, a) in cashflows) { if (a > 0) hasPos = true; if (a < 0) hasNeg = true; }
        if (!hasPos || !hasNeg) return 0m;

        var d0 = cashflows[0].Date;
        var times = new double[cashflows.Count];
        var amts = new double[cashflows.Count];
        for (var i = 0; i < cashflows.Count; i++)
        {
            times[i] = (cashflows[i].Date.DayNumber - d0.DayNumber) / 365.0;
            amts[i] = (double)cashflows[i].Amount;
        }

        foreach (var seed in new[] { 0.1, 0.0, -0.5, 1.0, -0.9 })
        {
            var r = seed;
            var converged = false;
            for (var iter = 0; iter < 100; iter++)
            {
                double f = 0, df = 0;
                for (var i = 0; i < amts.Length; i++)
                {
                    var pow = Math.Pow(1 + r, times[i]);
                    if (double.IsInfinity(pow) || double.IsNaN(pow) || pow == 0) { f = double.NaN; break; }
                    f += amts[i] / pow;
                    df -= amts[i] * times[i] / (pow * (1 + r));
                }
                if (double.IsNaN(f) || df == 0) break;
                var step = f / df;
                r -= step;
                if (Math.Abs(step) < 1e-7) { converged = true; break; }
                if (r <= -0.9999) { r = -0.9999; }
            }
            if (converged && !double.IsNaN(r) && !double.IsInfinity(r) && r > -0.99 && r < 100)
                return (decimal)Math.Round(r, 6);
        }
        return 0m;
    }
}

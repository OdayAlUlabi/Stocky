namespace Stocky.Api.Services;

/// <summary>
/// Pearson correlation across a set of symbols using their aligned daily
/// return streams. Used by the portfolio correlation matrix endpoint so the
/// user can see how diversified (or not) their holdings are.
/// </summary>
public static class CorrelationCalculator
{
    /// <summary>
    /// Pearson correlation between two return series of equal length.
    /// Returns 0 when n &lt; 2 or either series has zero variance.
    /// </summary>
    public static decimal Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Count < 2) return 0m;
        var n = x.Count;
        double meanX = 0, meanY = 0;
        for (var i = 0; i < n; i++) { meanX += x[i]; meanY += y[i]; }
        meanX /= n; meanY /= n;
        double num = 0, dx2 = 0, dy2 = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = x[i] - meanX;
            var dy = y[i] - meanY;
            num += dx * dy;
            dx2 += dx * dx;
            dy2 += dy * dy;
        }
        var denom = Math.Sqrt(dx2 * dy2);
        if (denom == 0 || double.IsNaN(denom) || double.IsInfinity(denom)) return 0m;
        var r = num / denom;
        if (r > 1) r = 1; else if (r < -1) r = -1;
        return Math.Round((decimal)r, 4);
    }

    /// <summary>
    /// Convert an aligned daily-close series into log returns (more stable for
    /// correlation than simple returns and standard in quant work).
    /// </summary>
    public static double[] LogReturns(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < 2) return Array.Empty<double>();
        var rets = new double[closes.Count - 1];
        for (var i = 1; i < closes.Count; i++)
        {
            var prev = (double)closes[i - 1];
            var curr = (double)closes[i];
            rets[i - 1] = (prev > 0 && curr > 0) ? Math.Log(curr / prev) : 0;
        }
        return rets;
    }
}

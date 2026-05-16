using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

public class CorrelationCalculatorTests
{
    [Fact]
    public void Pearson_PerfectlyCorrelated_IsOne()
    {
        var x = new double[] { 1, 2, 3, 4, 5 };
        var y = new double[] { 2, 4, 6, 8, 10 };
        Assert.Equal(1m, CorrelationCalculator.Pearson(x, y));
    }

    [Fact]
    public void Pearson_PerfectlyAnticorrelated_IsMinusOne()
    {
        var x = new double[] { 1, 2, 3, 4, 5 };
        var y = new double[] { 5, 4, 3, 2, 1 };
        Assert.Equal(-1m, CorrelationCalculator.Pearson(x, y));
    }

    [Fact]
    public void Pearson_ConstantSeries_IsZero()
    {
        var x = new double[] { 1, 1, 1, 1 };
        var y = new double[] { 4, 5, 6, 7 };
        Assert.Equal(0m, CorrelationCalculator.Pearson(x, y));
    }

    [Fact]
    public void Pearson_Uncorrelated_IsNearZero()
    {
        // Two near-orthogonal sequences.
        var x = new double[] { 1, -1, 1, -1, 1, -1 };
        var y = new double[] { 1, 1, -1, -1, 1, 1 };
        var r = CorrelationCalculator.Pearson(x, y);
        Assert.InRange((double)r, -0.5, 0.5);
    }

    [Fact]
    public void LogReturns_ProducesNMinusOneEntries()
    {
        var closes = new decimal[] { 100m, 110m, 121m, 121m };
        var rets = CorrelationCalculator.LogReturns(closes);
        Assert.Equal(3, rets.Length);
        Assert.InRange(rets[0], 0.094, 0.096);  // ln(1.1)
        Assert.InRange(rets[2], -0.001, 0.001); // flat
    }
}

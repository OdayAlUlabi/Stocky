using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

public class ReturnsCalculatorTests
{
    [Fact]
    public void Twrr_NoFlows_IsRawValueReturn()
    {
        // 100 → 110 → 121 with no external flows ≈ 21%.
        var points = new[]
        {
            (new DateOnly(2025, 1, 1), 100m, 0m),
            (new DateOnly(2025, 1, 2), 110m, 0m),
            (new DateOnly(2025, 1, 3), 121m, 0m),
        };
        var r = ReturnsCalculator.Twrr(points);
        Assert.InRange((double)r, 0.20, 0.22);
    }

    [Fact]
    public void Twrr_IgnoresContributions()
    {
        // Start 100, +50 inflow on day 2 jumps value to 165 (true 10% on day 2),
        // then steady. TWRR should reflect ~10% not the inflated raw return.
        var points = new[]
        {
            (new DateOnly(2025, 1, 1), 100m, 0m),
            (new DateOnly(2025, 1, 2), 165m, 50m), // (165-100-50)/100 = 0.15
            (new DateOnly(2025, 1, 3), 165m, 0m),  // 0%
        };
        var r = ReturnsCalculator.Twrr(points);
        Assert.InRange((double)r, 0.14, 0.16);
    }

    [Fact]
    public void Mwrr_SimpleDoubleOverOneYear_IsAbout100Percent()
    {
        var cf = new[]
        {
            (new DateOnly(2025, 1, 1), -100m),
            (new DateOnly(2026, 1, 1),  200m),
        };
        var r = ReturnsCalculator.Mwrr(cf);
        Assert.InRange((double)r, 0.99, 1.01);
    }

    [Fact]
    public void Mwrr_FlatReturn_IsZero()
    {
        var cf = new[]
        {
            (new DateOnly(2025, 1, 1), -100m),
            (new DateOnly(2026, 1, 1),  100m),
        };
        var r = ReturnsCalculator.Mwrr(cf);
        Assert.InRange((double)r, -0.001, 0.001);
    }

    [Fact]
    public void Mwrr_AllSameSign_ReturnsZero()
    {
        var cf = new[]
        {
            (new DateOnly(2025, 1, 1), -100m),
            (new DateOnly(2025, 6, 1), -50m),
        };
        Assert.Equal(0m, ReturnsCalculator.Mwrr(cf));
    }
}

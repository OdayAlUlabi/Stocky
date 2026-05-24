using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Controllers;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

/// <summary>
/// Advanced portfolio analytics — GitHub milestone #8 implementations:
/// VaR, stress, liquidity, concentration, momentum, optimizer, position sizing.
/// Pure math; deterministic stubs only.
/// </summary>
public class AdvancedPortfolioTests
{
    private static StockyDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<StockyDbContext>()
            .UseInMemoryDatabase($"adv-{Guid.NewGuid()}")
            .Options;
        return new StockyDbContext(options);
    }

    private static IAdvancedMarketDataProvider NewAdvanced() =>
        new StubAdvancedMarketDataProvider(new StubMarketDataProvider());

    // ---------- Position Sizing (4.3) ---------------------------------------
    [Fact]
    public void Position_sizing_fixed_fractional_matches_formula()
    {
        var svc = new PositionSizingService();
        // Account=$100k, 1% risk = $1000. Entry=$100, stop=$95 → per-share risk=$5.
        // Expected shares = floor(1000 / 5) = 200.
        var r = svc.Compute(new PositionSizingRequest(
            AccountSize: 100_000m, EntryPrice: 100m, StopLossPrice: 95m,
            RiskPerTradePercent: 0.01m));
        Assert.Equal(1000m, r.RiskPerTradeDollars);
        Assert.Equal(200, r.FixedFractionalShares);
        Assert.Equal(200 * 100m, r.FixedFractionalNotional);
    }

    [Fact]
    public void Position_sizing_kelly_uses_half_kelly_cap()
    {
        var svc = new PositionSizingService();
        // p=0.6, b=2 → f*=(0.6*2-0.4)/2 = 0.4 → half-Kelly=0.2
        // shares = floor(100k * 0.2 / 50) = 400
        var r = svc.Compute(new PositionSizingRequest(
            AccountSize: 100_000m, EntryPrice: 50m, StopLossPrice: 45m,
            RiskPerTradePercent: 0.01m,
            WinRate: 0.6m, AvgWinDollars: 200m, AvgLossDollars: 100m));
        Assert.Equal(0.2m, r.KellyFraction);
        Assert.Equal(400, r.KellyShares);
    }

    [Fact]
    public void Position_sizing_picks_smallest_nonzero_method()
    {
        var svc = new PositionSizingService();
        // ff = floor(1000/5)=200, kelly half=0.2 → 400, vol target 100k*(0.10/0.40)/50=500
        var r = svc.Compute(new PositionSizingRequest(
            AccountSize: 100_000m, EntryPrice: 50m, StopLossPrice: 45m,
            RiskPerTradePercent: 0.01m,
            WinRate: 0.6m, AvgWinDollars: 200m, AvgLossDollars: 100m,
            AssetVolatilityPercent: 0.40m, TargetVolatilityPercent: 0.10m));
        Assert.Equal(200, r.RecommendedShares);
        Assert.Equal("fixed-fractional", r.RecommendedMethod);
    }

    [Fact]
    public void Position_sizing_returns_zero_when_inputs_insufficient()
    {
        var svc = new PositionSizingService();
        var r = svc.Compute(new PositionSizingRequest(
            AccountSize: 100_000m, EntryPrice: 100m, StopLossPrice: 0m));
        Assert.Equal(0, r.FixedFractionalShares);
        Assert.Equal(0, r.RecommendedShares);
    }

    // ---------- Concentration / HHI (2.5) -----------------------------------
    [Fact]
    public async Task Concentration_HHI_equals_10000_for_single_position()
    {
        using var db = NewDb();
        var portfolioId = Guid.NewGuid();
        var ownerId = "user-1";
        db.Portfolios.Add(new Portfolio { Id = portfolioId, OwnerId = ownerId, Name = "p", BaseCurrency = "USD" });
        db.Holdings.Add(new Holding { Id = Guid.NewGuid(), PortfolioId = portfolioId, Symbol = "AAA", Quantity = 100m, AverageCost = 10m });
        db.PriceQuotes.Add(new PriceQuote { Symbol = "AAA", Price = 10m, AsOf = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var svc = new AdvancedRiskService(
            db,
            new PortfolioAnalyticsService(new PortfolioHistoryService(db, new StubMarketDataProvider()), new StubMarketDataProvider()),
            new PortfolioHistoryService(db, new StubMarketDataProvider()),
            NewAdvanced());

        var dto = await svc.BuildConcentrationAsync(portfolioId, ownerId, 0.10m, 0.30m, 0.40m);
        Assert.NotNull(dto);
        Assert.Equal(10000m, dto!.HhiScore);
        Assert.Equal("concentrated", dto.HhiInterpretation);
        // Single 100% position breaches max 10% position limit.
        Assert.Contains(dto.Breaches, b => b.Limit == "position");
    }

    [Fact]
    public async Task Concentration_HHI_equally_weighted_n_positions_equals_10000_over_n()
    {
        using var db = NewDb();
        var portfolioId = Guid.NewGuid();
        var ownerId = "user-2";
        db.Portfolios.Add(new Portfolio { Id = portfolioId, OwnerId = ownerId, Name = "p", BaseCurrency = "USD" });
        // 4 equally weighted positions @ $1000 each → HHI = 4 * 0.25^2 * 10000 = 2500.
        foreach (var s in new[] { "AAA", "BBB", "CCC", "DDD" })
        {
            db.Holdings.Add(new Holding { Id = Guid.NewGuid(), PortfolioId = portfolioId, Symbol = s, Quantity = 100m, AverageCost = 10m });
            db.PriceQuotes.Add(new PriceQuote { Symbol = s, Price = 10m, AsOf = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();

        var svc = new AdvancedRiskService(
            db,
            new PortfolioAnalyticsService(new PortfolioHistoryService(db, new StubMarketDataProvider()), new StubMarketDataProvider()),
            new PortfolioHistoryService(db, new StubMarketDataProvider()),
            NewAdvanced());

        var dto = await svc.BuildConcentrationAsync(portfolioId, ownerId, 0.40m, 1.0m, 1.0m);
        Assert.NotNull(dto);
        Assert.Equal(2500m, dto!.HhiScore);
        Assert.Equal("moderate", dto.HhiInterpretation);
        Assert.Equal(4, dto.Positions.Count);
        Assert.All(dto.Positions, p => Assert.Equal(0.25m, p.Weight));
    }

    // ---------- Stress Testing (2.2) ----------------------------------------
    [Fact]
    public async Task Stress_test_negative_equity_shock_produces_negative_portfolio_pnl()
    {
        using var db = NewDb();
        var portfolioId = Guid.NewGuid();
        var ownerId = "user-3";
        db.Portfolios.Add(new Portfolio { Id = portfolioId, OwnerId = ownerId, Name = "p", BaseCurrency = "USD" });
        db.Holdings.Add(new Holding { Id = Guid.NewGuid(), PortfolioId = portfolioId, Symbol = "AAA", Quantity = 100m, AverageCost = 10m });
        db.PriceQuotes.Add(new PriceQuote { Symbol = "AAA", Price = 10m, AsOf = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var svc = new AdvancedRiskService(
            db,
            new PortfolioAnalyticsService(new PortfolioHistoryService(db, new StubMarketDataProvider()), new StubMarketDataProvider()),
            new PortfolioHistoryService(db, new StubMarketDataProvider()),
            NewAdvanced());

        var dto = await svc.RunStressAsync(
            new StressTestRequest(portfolioId, ScenarioId: "gfc_2008", CustomShock: null),
            ownerId);
        Assert.NotNull(dto);
        Assert.True(dto!.PortfolioPnlDollars < 0);
        Assert.True(dto.PortfolioValueBefore > 0);
        Assert.Single(dto.HoldingImpacts);
        Assert.Equal("gfc_2008", dto.ScenarioId);
    }

    [Fact]
    public async Task Stress_test_custom_zero_shock_yields_zero_pnl()
    {
        using var db = NewDb();
        var portfolioId = Guid.NewGuid();
        var ownerId = "user-4";
        db.Portfolios.Add(new Portfolio { Id = portfolioId, OwnerId = ownerId, Name = "p", BaseCurrency = "USD" });
        db.Holdings.Add(new Holding { Id = Guid.NewGuid(), PortfolioId = portfolioId, Symbol = "AAA", Quantity = 50m, AverageCost = 20m });
        db.PriceQuotes.Add(new PriceQuote { Symbol = "AAA", Price = 20m, AsOf = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var svc = new AdvancedRiskService(
            db,
            new PortfolioAnalyticsService(new PortfolioHistoryService(db, new StubMarketDataProvider()), new StubMarketDataProvider()),
            new PortfolioHistoryService(db, new StubMarketDataProvider()),
            NewAdvanced());

        var dto = await svc.RunStressAsync(
            new StressTestRequest(portfolioId, null, new StressShockDto(0m, 0m, 0m, 0m, 0m)),
            ownerId);
        Assert.NotNull(dto);
        Assert.Equal(0m, dto!.PortfolioPnlDollars);
        Assert.Equal(0m, dto.PortfolioPnlPercent);
    }

    [Fact]
    public void Stress_preset_scenarios_are_defined_and_negative_for_crash_scenarios()
    {
        var presets = AdvancedRiskService.PresetScenarios;
        Assert.Contains(presets, s => s.Id == "gfc_2008");
        Assert.Contains(presets, s => s.Id == "covid_2020");
        Assert.Contains(presets, s => s.Id == "rate_shock_2022");
        Assert.Contains(presets, s => s.Id == "black_monday_1987");
        foreach (var s in presets)
            Assert.True(s.Shock.EquityShock < 0, $"{s.Id} should have negative equity shock");
    }

    // ---------- Liquidity Risk (2.4) ----------------------------------------
    [Fact]
    public async Task Liquidity_risk_returns_positions_and_value_weighted_score()
    {
        using var db = NewDb();
        var portfolioId = Guid.NewGuid();
        var ownerId = "user-5";
        db.Portfolios.Add(new Portfolio { Id = portfolioId, OwnerId = ownerId, Name = "p", BaseCurrency = "USD" });
        db.Holdings.Add(new Holding { Id = Guid.NewGuid(), PortfolioId = portfolioId, Symbol = "AAA", Quantity = 100m, AverageCost = 10m });
        await db.SaveChangesAsync();

        var svc = new AdvancedRiskService(
            db,
            new PortfolioAnalyticsService(new PortfolioHistoryService(db, new StubMarketDataProvider()), new StubMarketDataProvider()),
            new PortfolioHistoryService(db, new StubMarketDataProvider()),
            NewAdvanced());

        var dto = await svc.BuildLiquidityAsync(portfolioId, ownerId, 0.20m, 5, 30);
        Assert.NotNull(dto);
        Assert.Single(dto!.Positions);
        var pos = dto.Positions[0];
        Assert.Equal("AAA", pos.Symbol);
        Assert.True(pos.AverageDailyVolumeShares >= 0);
        Assert.True(pos.DaysToLiquidate >= 0);
    }

    // ---------- Momentum Scoring (1.3) --------------------------------------
    [Fact]
    public async Task Momentum_scores_are_ranked_and_in_0_100_range()
    {
        var market = new StubMarketDataProvider();
        var svc = new MomentumScoringService(market, new StubAdvancedMarketDataProvider(market));
        var dto = await svc.ScoreAsync(new MomentumRequest(
            Universe: new[] { "AAPL", "MSFT", "GOOG", "AMZN" },
            LookbackDays: new[] { 21, 63, 126 },
            VolatilityScale: true,
            SkipLatestMonth: true));
        Assert.NotNull(dto);
        Assert.Equal(4, dto!.Scores.Count);
        Assert.All(dto.Scores, s =>
        {
            Assert.InRange(s.CompositeScore, 0m, 100m);
            Assert.InRange(s.Rank, 1, 4);
        });
        // Ranks 1..N appear exactly once.
        var ranks = dto.Scores.Select(s => s.Rank).OrderBy(r => r).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4 }, ranks);
    }

    // ---------- Portfolio Optimizer (1.4) -----------------------------------
    [Fact]
    public async Task Optimizer_produces_weights_that_sum_to_one_and_respect_box_constraints()
    {
        var market = new StubMarketDataProvider();
        var svc = new PortfolioOptimizerService(market, new StubAdvancedMarketDataProvider(market));
        var dto = await svc.RunAsync(new OptimizerRequest(
            Symbols: new[] { "AAPL", "MSFT", "GOOG", "AMZN" },
            LookbackDays: 252,
            MaxWeight: 0.40m,
            MinWeight: 0m));
        Assert.NotNull(dto);
        var sumTangency = dto!.TangencyWeights.Sum(w => w.Weight);
        Assert.InRange(sumTangency, 0.999m, 1.001m);
        Assert.All(dto.TangencyWeights, w =>
        {
            Assert.InRange(w.Weight, 0m, 0.401m);
        });
        Assert.NotEmpty(dto.EfficientFrontier);
    }

    // ---------- VaR Suite (2.1) ---------------------------------------------
    [Fact]
    public void InverseStandardNormal_known_quantiles_match_table_values()
    {
        // Standard inverse-CDF values:
        //   N⁻¹(0.05)  ≈ -1.64485
        //   N⁻¹(0.01)  ≈ -2.32635
        //   N⁻¹(0.025) ≈ -1.95996
        var z05 = AdvancedRiskService.InverseStandardNormal(0.05);
        var z01 = AdvancedRiskService.InverseStandardNormal(0.01);
        var z025 = AdvancedRiskService.InverseStandardNormal(0.025);
        Assert.InRange(z05, -1.6460, -1.6437);
        Assert.InRange(z01, -2.3270, -2.3258);
        Assert.InRange(z025, -1.9610, -1.9590);
    }
}

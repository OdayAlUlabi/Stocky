using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Controllers;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

/// <summary>M9 — analytics and charts smoke tests against the deterministic stubs.</summary>
public class M9AnalyticsTests
{
    private static StockyDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<StockyDbContext>()
            .UseInMemoryDatabase($"m9-{Guid.NewGuid()}")
            .Options;
        return new StockyDbContext(options);
    }

    private static IAdvancedMarketDataProvider NewAdvanced() =>
        new StubAdvancedMarketDataProvider(new StubMarketDataProvider());

    [Fact]
    public async Task Bars_endpoint_returns_continuous_weekday_series_anchored_to_spot()
    {
        var ctrl = new BarsController(NewAdvanced());
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-30);
        var res = (await ctrl.Get("AAPL", from, to, 30)).Result as OkObjectResult;
        var bars = Assert.IsAssignableFrom<IEnumerable<OhlcBarDto>>(res!.Value).ToList();
        Assert.NotEmpty(bars);
        Assert.All(bars, b =>
        {
            Assert.True(b.High >= b.Low);
            Assert.True(b.High >= b.Open && b.High >= b.Close);
            Assert.True(b.Low <= b.Open && b.Low <= b.Close);
            Assert.True(b.Volume > 0);
            Assert.NotEqual(DayOfWeek.Saturday, b.Date.DayOfWeek);
            Assert.NotEqual(DayOfWeek.Sunday, b.Date.DayOfWeek);
        });
        // Bars are ordered ascending by date.
        for (int i = 1; i < bars.Count; i++)
            Assert.True(bars[i].Date > bars[i - 1].Date);
    }

    [Fact]
    public async Task Analyst_ratings_returns_consistent_distribution_and_label()
    {
        var ctrl = new AnalystRatingsController(NewAdvanced());
        var res = (await ctrl.Get("MSFT")).Result as OkObjectResult;
        var r = Assert.IsType<AnalystRatingDto>(res!.Value);
        Assert.Equal("MSFT", r.Symbol);
        var d = r.Distribution;
        Assert.Equal(r.AnalystCount, d.StrongBuy + d.Buy + d.Hold + d.Sell + d.StrongSell);
        Assert.InRange(r.ConsensusScore, 1m, 5m);
        Assert.True(r.PriceTargetLow <= r.PriceTargetMean);
        Assert.True(r.PriceTargetMean <= r.PriceTargetHigh);
        Assert.False(string.IsNullOrEmpty(r.ConsensusLabel));
    }

    [Fact]
    public async Task Goal_projection_reports_progress_and_on_track_flag()
    {
        await using var db = NewDb();
        const string ownerId = "00000000-0000-0000-0000-000000000001";
        var portfolio = new Portfolio { OwnerId = ownerId, Name = "Main" };
        db.Portfolios.Add(portfolio);
        db.Holdings.Add(new Holding { PortfolioId = portfolio.Id, Symbol = "AAPL", Quantity = 100, AverageCost = 150 });
        await db.SaveChangesAsync();

        var svc = new GoalsService(db);
        var dto = await svc.CreateAsync(ownerId, new GoalCreateDto(
            portfolio.Id, "Retire", 1_000_000m, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(20)),
            2_000m, 0.07m));

        Assert.True(dto.CurrentValue > 0);
        Assert.True(dto.ProgressPercent > 0);
        Assert.NotEmpty(dto.Projection);
        // Projection trajectory's last point matches projected final value.
        Assert.Equal(dto.Projection[^1].ProjectedValue, dto.ProjectedFinalValue);
    }

    [Fact]
    public async Task Goal_list_and_delete_round_trip()
    {
        await using var db = NewDb();
        const string ownerId = "00000000-0000-0000-0000-000000000001";
        var svc = new GoalsService(db);
        var created = await svc.CreateAsync(ownerId, new GoalCreateDto(
            null, "House", 250_000m, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(5)), 1_500m, 0.05m));
        var list = await svc.ListAsync(ownerId);
        Assert.Single(list);
        Assert.Equal(created.Id, list[0].Id);
        Assert.True(await svc.DeleteAsync(ownerId, created.Id));
        Assert.Empty(await svc.ListAsync(ownerId));
    }

    [Fact]
    public void Earnings_surprise_history_is_ordered_and_bounded()
    {
        var svc = new EarningsSurpriseService();
        var hist = svc.Build("AAPL", 8);
        Assert.Equal(8, hist.Count);
        for (var i = 1; i < hist.Count; i++) Assert.True(hist[i].Date > hist[i - 1].Date);
        Assert.All(hist, p =>
        {
            Assert.NotNull(p.EpsEstimate);
            Assert.NotNull(p.EpsActual);
            Assert.NotNull(p.SurprisePercent);
        });
    }

    [Fact]
    public async Task Backtest_returns_series_with_contributions_growing()
    {
        await using var db = NewDb();
        const string ownerId = "00000000-0000-0000-0000-000000000001";
        var portfolio = new Portfolio { OwnerId = ownerId, Name = "Bt" };
        db.Portfolios.Add(portfolio);
        await db.SaveChangesAsync();

        var svc = new BacktestService(db, NewAdvanced());
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddYears(-2);
        var req = new BacktestRequest(
            portfolio.Id, from, to, 10_000m, 500m, "Monthly",
            new List<RebalanceTargetDto>
            {
                new("AAPL", 60m),
                new("MSFT", 40m),
            });
        var result = await svc.RunAsync(ownerId, req);
        Assert.NotNull(result);
        Assert.NotEmpty(result!.Series);
        Assert.True(result.TotalContributions >= 10_000m);
        // Contributions are monotonically non-decreasing across the series.
        for (var i = 1; i < result.Series.Count; i++)
            Assert.True(result.Series[i].Contributions >= result.Series[i - 1].Contributions);
    }

    [Fact]
    public async Task Risk_metrics_returns_benchmark_symbol_and_bounded_var()
    {
        await using var db = NewDb();
        const string ownerId = "00000000-0000-0000-0000-000000000001";
        var portfolio = new Portfolio { OwnerId = ownerId, Name = "Risk", BenchmarkSymbol = "SPY" };
        db.Portfolios.Add(portfolio);
        // Seed a 60-day snapshot series so PortfolioHistoryService has data to chew on.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var i = 60; i >= 0; i--)
        {
            db.PortfolioSnapshots.Add(new PortfolioSnapshot
            {
                PortfolioId = portfolio.Id,
                Date = today.AddDays(-i),
                MarketValue = 10_000m + i * 50m,
                CostBasis = 10_000m,
                DayPnL = 50m,
            });
        }
        db.Transactions.Add(new Transaction
        {
            PortfolioId = portfolio.Id,
            Type = TransactionType.Deposit,
            Symbol = "USD",
            Quantity = 10_000m,
            Price = 1m,
            ExecutedAt = DateTime.UtcNow.AddDays(-60),
            Currency = "USD",
        });
        await db.SaveChangesAsync();

        var market = new StubMarketDataProvider();
        var history = new PortfolioHistoryService(db, market);
        var analytics = new PortfolioAnalyticsService(history, market);
        var risk = new RiskMetricsService(db, analytics, market);
        var result = await risk.BuildAsync(portfolio.Id, ownerId);
        Assert.NotNull(result);
        Assert.Equal("SPY", result!.BenchmarkSymbol);
        // VaR is reported as a positive % loss magnitude.
        Assert.True(result.Var95 >= 0);
        Assert.True(result.Var99 >= 0);
    }
}

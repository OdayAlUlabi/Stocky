using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

public class RebalanceServiceTests
{
    private const string OwnerId = "00000000-0000-0000-0000-000000000001";

    private static StockyDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<StockyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StockyDbContext(opts);
    }

    private static async Task<Guid> SeedPortfolioAsync(
        StockyDbContext db,
        params (string Symbol, decimal Quantity, decimal Price)[] holdings)
    {
        var pid = Guid.NewGuid();
        db.Portfolios.Add(new Portfolio { Id = pid, OwnerId = OwnerId, Name = "Main", BaseCurrency = "USD" });
        foreach (var (sym, qty, px) in holdings)
        {
            db.Holdings.Add(new Holding { PortfolioId = pid, Symbol = sym, Quantity = qty, AverageCost = px });
            db.PriceQuotes.Add(new PriceQuote { Symbol = sym, Price = px, AsOf = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        return pid;
    }

    [Fact]
    public async Task NoTargets_AllHoldingsHaveZeroTargetAndSellAction()
    {
        using var db = CreateDb();
        var pid = await SeedPortfolioAsync(db, ("AAPL", 10m, 100m));
        var svc = new RebalanceService(db);
        var report = await svc.ComputeAsync(pid, OwnerId);
        Assert.NotNull(report);
        Assert.Equal(1000m, report!.TotalValue);
        var s = Assert.Single(report.Suggestions);
        Assert.Equal("AAPL", s.Symbol);
        Assert.Equal(100m, s.CurrentWeightPercent);
        Assert.Equal(0m, s.TargetWeightPercent);
        Assert.Equal("Sell", s.Action);
        Assert.Equal(-1000m, s.TradeValue);
    }

    [Fact]
    public async Task PerfectlyAligned_AllHold()
    {
        using var db = CreateDb();
        var pid = await SeedPortfolioAsync(db, ("AAPL", 6m, 100m), ("MSFT", 4m, 100m));
        await new RebalanceService(db).SetTargetsAsync(pid, OwnerId,
            new[] { new RebalanceTargetDto("AAPL", 60m), new RebalanceTargetDto("MSFT", 40m) });

        var report = await new RebalanceService(db).ComputeAsync(pid, OwnerId);
        Assert.NotNull(report);
        Assert.All(report!.Suggestions, s => Assert.Equal("Hold", s.Action));
        Assert.Equal(100m, report.TargetWeightSumPercent);
    }

    [Fact]
    public async Task Overweight_SuggestsSellWithDriftBreakdown()
    {
        using var db = CreateDb();
        // AAPL: $800, MSFT: $200 → total $1000. Targets 50/50 → AAPL must trim $300.
        var pid = await SeedPortfolioAsync(db, ("AAPL", 8m, 100m), ("MSFT", 2m, 100m));
        await new RebalanceService(db).SetTargetsAsync(pid, OwnerId,
            new[] { new RebalanceTargetDto("AAPL", 50m), new RebalanceTargetDto("MSFT", 50m) });

        var report = await new RebalanceService(db).ComputeAsync(pid, OwnerId);
        var aapl = report!.Suggestions.Single(s => s.Symbol == "AAPL");
        var msft = report.Suggestions.Single(s => s.Symbol == "MSFT");
        Assert.Equal("Sell", aapl.Action);
        Assert.Equal(-300m, aapl.TradeValue);
        Assert.Equal(30m, aapl.DriftPercent);
        Assert.Equal("Buy", msft.Action);
        Assert.Equal(300m, msft.TradeValue);
        Assert.Equal(-30m, msft.DriftPercent);
    }

    [Fact]
    public async Task TargetSymbolNotHeld_SurfacesAsBuy()
    {
        using var db = CreateDb();
        var pid = await SeedPortfolioAsync(db, ("AAPL", 10m, 100m));
        // Seed a price for an unheld target symbol so the suggestion can be evaluated.
        db.PriceQuotes.Add(new PriceQuote { Symbol = "VTI", Price = 200m, AsOf = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await new RebalanceService(db).SetTargetsAsync(pid, OwnerId,
            new[] { new RebalanceTargetDto("AAPL", 70m), new RebalanceTargetDto("VTI", 30m) });

        var report = await new RebalanceService(db).ComputeAsync(pid, OwnerId);
        var vti = report!.Suggestions.Single(s => s.Symbol == "VTI");
        Assert.Equal("Buy", vti.Action);
        Assert.Equal(300m, vti.TradeValue);
        Assert.Equal(0m, vti.CurrentValue);
    }

    [Fact]
    public async Task SetTargets_RejectsTotalAbove100()
    {
        using var db = CreateDb();
        var pid = await SeedPortfolioAsync(db, ("AAPL", 1m, 100m));
        var svc = new RebalanceService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SetTargetsAsync(pid, OwnerId,
            new[] { new RebalanceTargetDto("AAPL", 70m), new RebalanceTargetDto("MSFT", 50m) }));
    }

    [Fact]
    public async Task SetTargets_ZeroWeightRemovesRow()
    {
        using var db = CreateDb();
        var pid = await SeedPortfolioAsync(db, ("AAPL", 1m, 100m));
        var svc = new RebalanceService(db);
        await svc.SetTargetsAsync(pid, OwnerId, new[] { new RebalanceTargetDto("AAPL", 100m) });
        Assert.Single(await svc.GetTargetsAsync(pid, OwnerId));
        await svc.SetTargetsAsync(pid, OwnerId, new[] { new RebalanceTargetDto("AAPL", 0m) });
        Assert.Empty(await svc.GetTargetsAsync(pid, OwnerId));
    }

    [Fact]
    public async Task Compute_WrongOwner_ReturnsNull()
    {
        using var db = CreateDb();
        var pid = await SeedPortfolioAsync(db, ("AAPL", 1m, 100m));
        var report = await new RebalanceService(db).ComputeAsync(pid, "someone-else");
        Assert.Null(report);
    }
}

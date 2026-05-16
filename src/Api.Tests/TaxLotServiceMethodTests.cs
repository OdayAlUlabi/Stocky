using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

public class TaxLotServiceMethodTests
{
    private static StockyDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<StockyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StockyDbContext(opts);
    }

    private static Transaction Tx(Guid pid, TransactionType type, decimal qty, decimal px, DateTimeOffset at) =>
        new()
        {
            Id = Guid.NewGuid(),
            PortfolioId = pid,
            Type = type,
            Symbol = "AAPL",
            Quantity = qty,
            Price = px,
            ExecutedAt = at,
            Currency = "USD",
        };

    /// <summary>
    /// Seed three buys at $10, $20, $30 (10 shares each) on consecutive days,
    /// then sell 10 shares at $25. Realised gain differs by lot-method.
    /// </summary>
    private static async Task<Guid> SeedAsync(StockyDbContext db, CostBasisMethod method)
    {
        var pid = Guid.NewGuid();
        db.Portfolios.Add(new Portfolio
        {
            Id = pid, OwnerId = "owner", Name = "P", BaseCurrency = "USD", CostBasisMethod = method,
        });
        var t0 = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        db.Transactions.AddRange(
            Tx(pid, TransactionType.Buy, 10m, 10m, t0),
            Tx(pid, TransactionType.Buy, 10m, 20m, t0.AddDays(1)),
            Tx(pid, TransactionType.Buy, 10m, 30m, t0.AddDays(2)),
            Tx(pid, TransactionType.Sell, 10m, 25m, t0.AddDays(3)));
        await db.SaveChangesAsync();
        return pid;
    }

    [Theory]
    [InlineData(CostBasisMethod.Fifo, 150)]      // sells the $10 lot → gain = (25-10)*10
    [InlineData(CostBasisMethod.Lifo, -50)]      // sells the $30 lot → gain = (25-30)*10
    [InlineData(CostBasisMethod.HighestCost, -50)] // same as LIFO here (the $30 lot)
    [InlineData(CostBasisMethod.LowestCost, 150)]  // same as FIFO here (the $10 lot)
    public async Task SelectsLot_PerMethod(CostBasisMethod method, decimal expectedGain)
    {
        using var db = CreateDb();
        var pid = await SeedAsync(db, method);
        await new TaxLotService(db).RecomputeAsync(pid);

        var gains = await db.RealizedGains.Where(g => g.PortfolioId == pid).ToListAsync();
        var realised = Assert.Single(gains);
        Assert.Equal(expectedGain, realised.Gain);

        var openLots = await db.TaxLots.Where(l => l.PortfolioId == pid && l.RemainingQuantity > 0).ToListAsync();
        Assert.Equal(2, openLots.Count);
        Assert.Equal(20m, openLots.Sum(l => l.RemainingQuantity));
    }

    [Fact]
    public async Task ChangingMethod_RecomputesFromScratch()
    {
        using var db = CreateDb();
        var pid = await SeedAsync(db, CostBasisMethod.Fifo);
        var svc = new TaxLotService(db);
        await svc.RecomputeAsync(pid);
        Assert.Equal(150m, (await db.RealizedGains.SingleAsync(g => g.PortfolioId == pid)).Gain);

        var p = await db.Portfolios.FirstAsync(x => x.Id == pid);
        p.CostBasisMethod = CostBasisMethod.HighestCost;
        await db.SaveChangesAsync();
        await svc.RecomputeAsync(pid);

        Assert.Equal(-50m, (await db.RealizedGains.SingleAsync(g => g.PortfolioId == pid)).Gain);
    }
}

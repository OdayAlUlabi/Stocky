using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

public class PortfolioLedgerServiceTests
{
    private static StockyDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<StockyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StockyDbContext(opts);
    }

    private static Transaction Tx(Guid pid, TransactionType type, string? symbol, decimal qty, decimal price, decimal fee = 0, DateTimeOffset? at = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            PortfolioId = pid,
            Type = type,
            Symbol = symbol,
            Quantity = qty,
            Price = price,
            Fee = fee,
            ExecutedAt = at ?? DateTimeOffset.UtcNow,
            Currency = "USD",
        };

    [Fact]
    public async Task GetCashBalance_AllTypes()
    {
        using var db = CreateDb();
        var pid = Guid.NewGuid();
        db.Transactions.AddRange(
            Tx(pid, TransactionType.Deposit, null, 1, 10_000m),
            Tx(pid, TransactionType.Buy, "AAPL", 10, 150m, fee: 1m),     // -1501
            Tx(pid, TransactionType.Dividend, "AAPL", 1, 25m),           // +25
            Tx(pid, TransactionType.Sell, "AAPL", 5, 180m, fee: 1m),     //  +899
            Tx(pid, TransactionType.Fee, null, 1, 5m),                   //   -5
            Tx(pid, TransactionType.Withdrawal, null, 1, 100m),          // -100
            Tx(pid, TransactionType.Split, "AAPL", 0, 0.5m),             //   0
            Tx(pid, TransactionType.SpinOff, "BB", 5, 0m)                //   0
        );
        await db.SaveChangesAsync();

        var svc = new PortfolioLedgerService(db);
        var cash = await svc.GetCashBalanceAsync(pid);

        Assert.Equal(10_000m - 1501m + 25m + 899m - 5m - 100m, cash);
    }

    [Fact]
    public async Task RecomputeHoldings_ReverseSplit_Scales()
    {
        using var db = CreateDb();
        var pid = Guid.NewGuid();
        db.Transactions.AddRange(
            Tx(pid, TransactionType.Buy, "LCID", 1000, 2.90m, at: DateTimeOffset.UtcNow.AddDays(-30)),
            Tx(pid, TransactionType.Split, "LCID", 0, 10m, at: DateTimeOffset.UtcNow.AddDays(-1))
        );
        await db.SaveChangesAsync();

        var svc = new PortfolioLedgerService(db);
        await svc.RecomputeHoldingsAsync(pid);
        await db.SaveChangesAsync();

        var h = db.Holdings.Single(x => x.Symbol == "LCID");
        Assert.Equal(100m, h.Quantity);
        Assert.Equal(29.00m, Math.Round(h.AverageCost, 2));
    }

    [Fact]
    public async Task RecomputeHoldings_SpinOff_CreatesZeroCostHolding()
    {
        using var db = CreateDb();
        var pid = Guid.NewGuid();
        db.Transactions.AddRange(
            Tx(pid, TransactionType.Buy, "OPEN", 100, 5m, at: DateTimeOffset.UtcNow.AddDays(-10)),
            Tx(pid, TransactionType.SpinOff, "OPENZ", 50, 0m, at: DateTimeOffset.UtcNow.AddDays(-1))
        );
        await db.SaveChangesAsync();

        var svc = new PortfolioLedgerService(db);
        await svc.RecomputeHoldingsAsync(pid);
        await db.SaveChangesAsync();

        var warrant = db.Holdings.Single(x => x.Symbol == "OPENZ");
        Assert.Equal(50m, warrant.Quantity);
        Assert.Equal(0m, warrant.AverageCost);
    }

    [Fact]
    public async Task RecomputeHoldings_SellAll_RemovesHolding()
    {
        using var db = CreateDb();
        var pid = Guid.NewGuid();
        db.Transactions.AddRange(
            Tx(pid, TransactionType.Buy, "MSFT", 10, 300m, at: DateTimeOffset.UtcNow.AddDays(-10)),
            Tx(pid, TransactionType.Sell, "MSFT", 10, 350m, at: DateTimeOffset.UtcNow.AddDays(-1))
        );
        await db.SaveChangesAsync();

        var svc = new PortfolioLedgerService(db);
        await svc.RecomputeHoldingsAsync(pid);
        await db.SaveChangesAsync();

        Assert.Empty(db.Holdings.Where(h => h.Symbol == "MSFT"));
    }
}

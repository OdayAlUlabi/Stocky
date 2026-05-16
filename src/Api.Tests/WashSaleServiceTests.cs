using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

public class WashSaleServiceTests
{
    private static StockyDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<StockyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StockyDbContext(opts);
    }

    private static Transaction Buy(Guid pid, string sym, decimal qty, decimal px, DateTimeOffset at) =>
        new()
        {
            Id = Guid.NewGuid(),
            PortfolioId = pid,
            Type = TransactionType.Buy,
            Symbol = sym,
            Quantity = qty,
            Price = px,
            ExecutedAt = at,
            Currency = "USD",
        };

    private static RealizedGain Loss(Guid pid, string sym, decimal qty, decimal loss, DateTimeOffset soldAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            PortfolioId = pid,
            Symbol = sym,
            SellTransactionId = Guid.NewGuid(),
            LotId = Guid.NewGuid(),
            AcquiredAt = soldAt.AddDays(-60),
            SoldAt = soldAt,
            Quantity = qty,
            CostBasis = 1000m,
            Proceeds = 1000m + loss,
            Gain = loss,
            IsLongTerm = false,
        };

    [Fact]
    public async Task NoLosses_ReturnsEmptyReport()
    {
        using var db = CreateDb();
        var pid = Guid.NewGuid();
        await db.SaveChangesAsync();
        var report = await new WashSaleService(db).ComputeAsync(pid, 2025);
        Assert.Empty(report.Adjustments);
        Assert.Equal(0m, report.TotalLoss);
        Assert.Equal(0m, report.DisallowedLoss);
    }

    [Fact]
    public async Task LossWithNoReplacementBuy_IsAllAllowed()
    {
        using var db = CreateDb();
        var pid = Guid.NewGuid();
        var sold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        db.RealizedGains.Add(Loss(pid, "AAPL", 10m, -200m, sold));
        await db.SaveChangesAsync();

        var report = await new WashSaleService(db).ComputeAsync(pid, 2025);
        Assert.Empty(report.Adjustments);
        Assert.Equal(-200m, report.TotalLoss);
        Assert.Equal(0m, report.DisallowedLoss);
    }

    [Fact]
    public async Task FullReplacementWithinWindow_DisallowsEntireLoss()
    {
        using var db = CreateDb();
        var pid = Guid.NewGuid();
        var sold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        db.RealizedGains.Add(Loss(pid, "AAPL", 10m, -200m, sold));
        db.Transactions.Add(Buy(pid, "AAPL", 10m, 95m, sold.AddDays(5))); // within +30
        await db.SaveChangesAsync();

        var report = await new WashSaleService(db).ComputeAsync(pid, 2025);
        var adj = Assert.Single(report.Adjustments);
        Assert.Equal(10m, adj.ReplacementShares);
        Assert.Equal(200m, adj.DisallowedLoss);
        Assert.Equal(0m, adj.AllowedLoss);
        Assert.Equal(-200m, report.DisallowedLoss);
    }

    [Fact]
    public async Task PartialReplacement_ProRatesDisallowedLoss()
    {
        using var db = CreateDb();
        var pid = Guid.NewGuid();
        var sold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        db.RealizedGains.Add(Loss(pid, "AAPL", 10m, -200m, sold));
        db.Transactions.Add(Buy(pid, "AAPL", 4m, 95m, sold.AddDays(10)));
        await db.SaveChangesAsync();

        var report = await new WashSaleService(db).ComputeAsync(pid, 2025);
        var adj = Assert.Single(report.Adjustments);
        Assert.Equal(4m, adj.ReplacementShares);
        Assert.Equal(80m, adj.DisallowedLoss);   // 200 * 4/10
        Assert.Equal(-120m, adj.AllowedLoss);    // -200 + 80
    }

    [Fact]
    public async Task BuyOutsideWindow_IsIgnored()
    {
        using var db = CreateDb();
        var pid = Guid.NewGuid();
        var sold = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        db.RealizedGains.Add(Loss(pid, "AAPL", 10m, -200m, sold));
        db.Transactions.Add(Buy(pid, "AAPL", 10m, 95m, sold.AddDays(45))); // beyond +30
        await db.SaveChangesAsync();

        var report = await new WashSaleService(db).ComputeAsync(pid, 2025);
        Assert.Empty(report.Adjustments);
    }

    [Fact]
    public async Task ReplacementSharesAllocatedFifoAcrossLosses()
    {
        using var db = CreateDb();
        var pid = Guid.NewGuid();
        var sold1 = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var sold2 = new DateTimeOffset(2025, 6, 10, 0, 0, 0, TimeSpan.Zero);
        db.RealizedGains.Add(Loss(pid, "AAPL", 10m, -100m, sold1));
        db.RealizedGains.Add(Loss(pid, "AAPL", 10m, -100m, sold2));
        // Single replacement buy of 10 shares — should fully cover first loss only.
        db.Transactions.Add(Buy(pid, "AAPL", 10m, 95m, sold1.AddDays(2)));
        await db.SaveChangesAsync();

        var report = await new WashSaleService(db).ComputeAsync(pid, 2025);
        var adj = Assert.Single(report.Adjustments);
        Assert.Equal(sold1, adj.SoldAt);
        Assert.Equal(100m, adj.DisallowedLoss);
        // Second loss had no replacement shares left → no adjustment row.
        Assert.Equal(-200m, report.TotalLoss);
        Assert.Equal(-100m, report.DisallowedLoss);
    }
}

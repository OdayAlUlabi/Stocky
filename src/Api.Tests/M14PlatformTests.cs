using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

/// <summary>M14 — Platform & Admin unit tests.</summary>
public class M14PlatformTests
{
    private static StockyDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<StockyDbContext>()
            .UseInMemoryDatabase($"m14-{Guid.NewGuid()}")
            .Options;
        return new StockyDbContext(options);
    }

    private static Portfolio Seed(StockyDbContext db, string ownerId = "u1")
    {
        var p = new Portfolio { OwnerId = ownerId, Name = "Test", BaseCurrency = "USD" };
        db.Portfolios.Add(p);
        db.SaveChanges();
        return p;
    }

    // -------- CashService ------------------------------------------------

    [Fact]
    public void ParseType_recognizes_aliases()
    {
        Assert.Equal(TransactionType.Deposit, CashService.ParseType("deposit"));
        Assert.Equal(TransactionType.Withdrawal, CashService.ParseType("withdraw"));
        Assert.Equal(TransactionType.Withdrawal, CashService.ParseType("WITHDRAWAL"));
        Assert.Equal(TransactionType.Fee, CashService.ParseType("Fee"));
        Assert.Equal(TransactionType.Dividend, CashService.ParseType("dividend"));
        Assert.Equal(TransactionType.Dividend, CashService.ParseType("interest"));
        Assert.Throws<ArgumentException>(() => CashService.ParseType("bogus"));
    }

    [Fact]
    public void SignedAmount_sets_correct_sign()
    {
        Assert.Equal(100m, CashService.SignedAmount(TransactionType.Deposit, 100m));
        Assert.Equal(50m, CashService.SignedAmount(TransactionType.Dividend, 50m));
        Assert.Equal(-25m, CashService.SignedAmount(TransactionType.Withdrawal, 25m));
        Assert.Equal(-7.5m, CashService.SignedAmount(TransactionType.Fee, 7.5m));
        // Magnitude is always derived; passing a negative still produces correct sign.
        Assert.Equal(-25m, CashService.SignedAmount(TransactionType.Withdrawal, -25m));
    }

    [Fact]
    public async Task CashService_balances_sum_signed_amounts_per_currency()
    {
        using var db = NewDb();
        var p = Seed(db);
        db.Transactions.AddRange(
            new Transaction { PortfolioId = p.Id, Type = TransactionType.Deposit, Quantity = 1, Price = 1000m, Currency = "USD", ExecutedAt = DateTimeOffset.UtcNow.AddDays(-3) },
            new Transaction { PortfolioId = p.Id, Type = TransactionType.Withdrawal, Quantity = 1, Price = 200m, Currency = "USD", ExecutedAt = DateTimeOffset.UtcNow.AddDays(-2) },
            new Transaction { PortfolioId = p.Id, Type = TransactionType.Fee, Quantity = 1, Price = 5m, Currency = "USD", ExecutedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new Transaction { PortfolioId = p.Id, Type = TransactionType.Dividend, Quantity = 1, Price = 12m, Currency = "USD", ExecutedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var svc = new CashService(db);
        var bal = await svc.BalancesAsync(p.Id, "u1");
        var usd = Assert.Single(bal);
        Assert.Equal("USD", usd.Currency);
        Assert.Equal(1000m - 200m - 5m + 12m, usd.Balance);
        Assert.Equal(4, usd.Count);
    }

    [Fact]
    public async Task CashService_ignores_non_cash_transactions()
    {
        using var db = NewDb();
        var p = Seed(db);
        db.Transactions.Add(new Transaction
        {
            PortfolioId = p.Id,
            Type = TransactionType.Buy,
            Symbol = "AAPL",
            Quantity = 10,
            Price = 150m,
            Currency = "USD"
        });
        await db.SaveChangesAsync();
        var svc = new CashService(db);
        var rows = await svc.ListAsync(p.Id, "u1");
        Assert.Empty(rows);
    }

    // -------- ModelPortfolioTemplates ------------------------------------

    [Fact]
    public void Templates_all_sum_to_100_percent()
    {
        foreach (var t in ModelPortfolioTemplates.All)
        {
            var total = t.Allocations.Sum(a => a.WeightPercent);
            Assert.True(Math.Abs(total - 100m) < 0.0001m, $"Template {t.Slug} weights sum to {total}, expected 100.");
            Assert.NotEmpty(t.Allocations);
        }
    }

    [Fact]
    public void Templates_lookup_by_slug_is_case_insensitive()
    {
        Assert.NotNull(ModelPortfolioTemplates.FindBySlug("bogleheads-3-fund"));
        Assert.NotNull(ModelPortfolioTemplates.FindBySlug("BOGLEHEADS-3-FUND"));
        Assert.Null(ModelPortfolioTemplates.FindBySlug("nope"));
    }

    // -------- AuditLogger -----------------------------------------------

    [Fact]
    public async Task AuditLogger_persists_entry_with_request_context()
    {
        using var db = NewDb();
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                Request = { Method = "POST", Path = "/api/test" }
            }
        };
        var logger = new AuditLogger(db, accessor, NullLogger<AuditLogger>.Instance);
        await logger.WriteAsync("u1", "create", "Portfolio", "abc", new { foo = 1 }, 201);

        var row = Assert.Single(await db.AuditEntries.ToListAsync());
        Assert.Equal("u1", row.OwnerId);
        Assert.Equal("create", row.Action);
        Assert.Equal("Portfolio", row.Resource);
        Assert.Equal("abc", row.ResourceId);
        Assert.Equal("POST", row.Method);
        Assert.Equal("/api/test", row.Path);
        Assert.Equal(201, row.StatusCode);
        Assert.NotNull(row.Details);
        Assert.Contains("foo", row.Details);
    }

    [Fact]
    public async Task AuditLogger_swallows_errors_to_avoid_breaking_mutations()
    {
        // Dispose the context so saves throw; logger should not propagate.
        var db = NewDb();
        db.Dispose();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var logger = new AuditLogger(db, accessor, NullLogger<AuditLogger>.Instance);
        await logger.WriteAsync("u1", "create", "Anything"); // must not throw
    }

    // -------- PositionNote round-trip ------------------------------------

    [Fact]
    public async Task PositionNote_round_trip_supports_create_update_delete()
    {
        using var db = NewDb();
        var n = new PositionNote { OwnerId = "u1", Symbol = "AAPL", Body = "Initial thesis" };
        db.PositionNotes.Add(n);
        await db.SaveChangesAsync();

        var fromDb = await db.PositionNotes.FirstAsync(x => x.Id == n.Id);
        fromDb.Body = "Updated thesis";
        fromDb.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal("Updated thesis", (await db.PositionNotes.FindAsync(n.Id))!.Body);

        db.PositionNotes.Remove(fromDb);
        await db.SaveChangesAsync();
        Assert.Empty(await db.PositionNotes.ToListAsync());
    }
}

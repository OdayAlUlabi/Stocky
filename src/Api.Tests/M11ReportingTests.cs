using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;
using Xunit;

namespace Stocky.Api.Tests;

/// <summary>M11 — Reporting & Sharing unit tests.</summary>
public class M11ReportingTests
{
    private static StockyDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<StockyDbContext>()
            .UseInMemoryDatabase($"m11-{Guid.NewGuid()}")
            .Options;
        return new StockyDbContext(options);
    }

    private static Portfolio Seed(StockyDbContext db, string ownerId = "u1")
    {
        var p = new Portfolio { OwnerId = ownerId, Name = "Test", BaseCurrency = "USD" };
        db.Portfolios.Add(p);
        db.Holdings.Add(new Holding { PortfolioId = p.Id, Symbol = "AAPL", Quantity = 10, AverageCost = 150m });
        db.Transactions.Add(new Transaction
        {
            PortfolioId = p.Id,
            Type = TransactionType.Dividend,
            Symbol = "AAPL",
            Quantity = 1m,
            Price = 5m,
            Currency = "USD",
            ExecutedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        db.SaveChanges();
        return p;
    }

    // -------- ShareTokenService ------------------------------------------

    [Fact]
    public void NewToken_is_url_safe_and_unique()
    {
        var a = ShareTokenService.NewToken();
        var b = ShareTokenService.NewToken();
        Assert.NotEqual(a, b);
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('=', a);
        Assert.True(a.Length >= 30);
    }

    [Fact]
    public async Task Create_share_token_persists_with_active_state()
    {
        using var db = NewDb();
        var p = Seed(db);
        var svc = new ShareTokenService(db);
        var issued = await svc.CreateAsync("u1", new CreateShareTokenRequest(p.Id, "Advisor", null, false, true));
        var st = issued.Record;
        Assert.False(string.IsNullOrEmpty(issued.Plaintext));
        Assert.True(st.IsActive(DateTimeOffset.UtcNow));
        Assert.True(st.IncludeCostBasis);
        Assert.False(st.IncludeTransactions);
        Assert.Equal(p.Id, st.PortfolioId);
    }

    [Fact]
    public async Task Resolve_returns_null_after_revoke()
    {
        using var db = NewDb();
        var p = Seed(db);
        var svc = new ShareTokenService(db);
        var issued = await svc.CreateAsync("u1", new CreateShareTokenRequest(p.Id, null, null));
        var ok = await svc.RevokeAsync("u1", issued.Record.Id);
        Assert.True(ok);
        var resolved = await svc.ResolveAsync(issued.Plaintext);
        Assert.Null(resolved);
    }

    [Fact]
    public async Task Resolve_returns_null_when_expired()
    {
        using var db = NewDb();
        var p = Seed(db);
        var svc = new ShareTokenService(db);
        var issued = await svc.CreateAsync("u1", new CreateShareTokenRequest(p.Id, null, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var resolved = await svc.ResolveAsync(issued.Plaintext);
        Assert.Null(resolved);
    }

    [Fact]
    public async Task Resolve_increments_view_count()
    {
        using var db = NewDb();
        var p = Seed(db);
        var svc = new ShareTokenService(db);
        var issued = await svc.CreateAsync("u1", new CreateShareTokenRequest(p.Id, null, null));
        await svc.ResolveAsync(issued.Plaintext);
        await svc.ResolveAsync(issued.Plaintext);
        var fresh = await db.ShareTokens.FirstAsync(s => s.Id == issued.Record.Id);
        Assert.Equal(2, fresh.ViewCount);
        Assert.NotNull(fresh.LastViewedAt);
    }

    // -------- Renderer ----------------------------------------------------

    [Fact]
    public async Task Renderer_dividends_csv_has_header_and_row()
    {
        using var db = NewDb();
        var p = Seed(db);
        var renderer = new ReportRenderer(db, new WashSaleService(db));
        var rendered = await renderer.RenderAsync(p.Id, ReportType.Dividends, ReportFormat.Csv);
        Assert.Equal("text/csv", rendered.ContentType);
        Assert.EndsWith(".csv", rendered.FileName);
        Assert.Contains("Symbol,Date,Amount,Currency", rendered.Body);
        Assert.Contains("AAPL", rendered.Body);
    }

    [Fact]
    public async Task Renderer_pdf_format_uses_pdf_header_and_content_type()
    {
        using var db = NewDb();
        var p = Seed(db);
        var renderer = new ReportRenderer(db, new WashSaleService(db));
        var rendered = await renderer.RenderAsync(p.Id, ReportType.Dividends, ReportFormat.Pdf);
        Assert.Equal("application/pdf", rendered.ContentType);
        Assert.EndsWith(".pdf", rendered.FileName);
        Assert.StartsWith("%PDF-1.4", rendered.Body);
        Assert.Contains("%%EOF", rendered.Body);
    }

    // -------- Cadence + ScheduleJob ---------------------------------------

    [Fact]
    public void Advance_weekly_adds_seven_days()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(from.AddDays(7), ReportScheduleJob.Advance(from, ReportCadence.Weekly));
        Assert.Equal(from.AddMonths(1), ReportScheduleJob.Advance(from, ReportCadence.Monthly));
        Assert.Equal(from.AddMonths(3), ReportScheduleJob.Advance(from, ReportCadence.Quarterly));
    }

    [Fact]
    public async Task ScheduleJob_runs_due_schedule_and_advances_next_run()
    {
        using var db = NewDb();
        var p = Seed(db);
        var renderer = new ReportRenderer(db, new WashSaleService(db));

        var sched = new ReportSchedule
        {
            OwnerId = "u1",
            PortfolioId = p.Id,
            Type = ReportType.Dividends,
            Format = ReportFormat.Csv,
            Cadence = ReportCadence.Weekly,
            Enabled = true,
            NextRunUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        db.ReportSchedules.Add(sched);
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(renderer);
        // ServiceScopeFactory wiring for the job:
        var sp = services.BuildServiceProvider();
        // Use a scope factory that always returns the same db instance.
        var job = new ReportScheduleJob(new SingleScopeFactory(db, renderer), new ConfigurationBuilder().Build(), NullLogger<ReportScheduleJob>.Instance);
        var processed = await job.RunOnceAsync(default);
        Assert.Equal(1, processed);

        var refreshed = await db.ReportSchedules.FirstAsync(s => s.Id == sched.Id);
        Assert.NotNull(refreshed.LastRunUtc);
        Assert.True(refreshed.NextRunUtc > DateTimeOffset.UtcNow);
        var delivery = await db.ReportDeliveries.FirstAsync();
        Assert.Equal(sched.Id, delivery.ScheduleId);
        Assert.Equal("schedule", delivery.Trigger);
        Assert.True(delivery.SizeBytes > 0);
    }

    private sealed class SingleScopeFactory(StockyDbContext db, ReportRenderer renderer) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(db, renderer);
        private sealed class Scope(StockyDbContext db, ReportRenderer renderer) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public void Dispose() { }
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(StockyDbContext)) return db;
                if (serviceType == typeof(ReportRenderer)) return renderer;
                return null;
            }
        }
    }
}

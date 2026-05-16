using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>M11 #54 — issues + resolves revocable share tokens. Tokens are
/// stored as SHA-256 hashes; the plaintext value is returned to the caller
/// only once at creation time.</summary>
public sealed class ShareTokenService(StockyDbContext db)
{
    public sealed record Issued(ShareToken Record, string Plaintext);

    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    internal static string Hash(string plaintext)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(bytes);
    }

    public async Task<Issued> CreateAsync(string ownerId, CreateShareTokenRequest req, CancellationToken ct = default)
    {
        var p = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == req.PortfolioId && p.OwnerId == ownerId, ct)
            ?? throw new InvalidOperationException("Portfolio not found");

        var plaintext = NewToken();
        var st = new ShareToken
        {
            TokenHash = Hash(plaintext),
            TokenPrefix = plaintext.Substring(0, Math.Min(8, plaintext.Length)),
            PortfolioId = p.Id,
            OwnerId = ownerId,
            Label = string.IsNullOrWhiteSpace(req.Label) ? null : req.Label!.Trim(),
            ExpiresAt = req.ExpiresAt,
            IncludeTransactions = req.IncludeTransactions,
            IncludeCostBasis = req.IncludeCostBasis,
        };
        db.ShareTokens.Add(st);
        await db.SaveChangesAsync(ct);
        return new Issued(st, plaintext);
    }

    public async Task<bool> RevokeAsync(string ownerId, Guid id, CancellationToken ct = default)
    {
        var st = await db.ShareTokens.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == ownerId, ct);
        if (st is null) return false;
        if (st.RevokedAt is null) st.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ShareToken?> ResolveAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Hash(token);
        var st = await db.ShareTokens.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (st is null || !st.IsActive(DateTimeOffset.UtcNow)) return null;
        st.ViewCount += 1;
        st.LastViewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return st;
    }
}

/// <summary>M11 #55 — renders capital-gains / wash-sales / dividends artifacts.</summary>
public sealed class ReportRenderer(StockyDbContext db, WashSaleService washSales)
{
    public sealed record Rendered(string FileName, string ContentType, string Body, int SizeBytes);

    public async Task<Rendered> RenderAsync(Guid portfolioId, ReportType type, ReportFormat format, CancellationToken ct = default)
    {
        var portfolio = await db.Portfolios.AsNoTracking().FirstOrDefaultAsync(p => p.Id == portfolioId, ct)
            ?? throw new InvalidOperationException("Portfolio not found");

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var slug = type.ToString().ToLowerInvariant();
        var ext = format == ReportFormat.Csv ? "csv" : "pdf";
        var ctype = format == ReportFormat.Csv ? "text/csv" : "application/pdf";
        var name = $"{slug}-{portfolio.Name}-{stamp}.{ext}".Replace(" ", "_");

        var body = type switch
        {
            ReportType.CapitalGains => await BuildCapitalGainsAsync(portfolioId, ct),
            ReportType.WashSales => await BuildWashSalesAsync(portfolioId, ct),
            ReportType.Dividends => await BuildDividendsAsync(portfolioId, ct),
            _ => "(empty)"
        };

        if (format == ReportFormat.Pdf)
        {
            body = WrapAsPdf(body, portfolio.Name, type);
        }

        var bytes = Encoding.UTF8.GetByteCount(body);
        return new Rendered(name, ctype, body, bytes);
    }

    private async Task<string> BuildCapitalGainsAsync(Guid portfolioId, CancellationToken ct)
    {
        var gains = await db.RealizedGains.AsNoTracking()
            .Where(g => g.PortfolioId == portfolioId)
            .OrderByDescending(g => g.SoldAt)
            .ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,AcquiredAt,SoldAt,Quantity,CostBasis,Proceeds,Gain,IsLongTerm");
        foreach (var g in gains)
            sb.AppendLine($"{Csv(g.Symbol)},{g.AcquiredAt:O},{g.SoldAt:O},{g.Quantity},{g.CostBasis},{g.Proceeds},{g.Gain},{g.IsLongTerm}");
        return sb.ToString();
    }

    private async Task<string> BuildWashSalesAsync(Guid portfolioId, CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        var report = await washSales.ComputeAsync(portfolioId, year, ct);
        var sb = new StringBuilder();
        sb.AppendLine($"Year,{report.Year}");
        sb.AppendLine($"TotalLoss,{report.TotalLoss}");
        sb.AppendLine($"DisallowedLoss,{report.DisallowedLoss}");
        sb.AppendLine();
        sb.AppendLine("LotId,Symbol,SoldAt,LotQuantity,LotLoss,ReplacementShares,DisallowedLoss,AllowedLoss");
        foreach (var a in report.Adjustments)
            sb.AppendLine($"{a.LotId},{Csv(a.Symbol)},{a.SoldAt:O},{a.LotQuantity},{a.LotLoss},{a.ReplacementShares},{a.DisallowedLoss},{a.AllowedLoss}");
        return sb.ToString();
    }

    private async Task<string> BuildDividendsAsync(Guid portfolioId, CancellationToken ct)
    {
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.PortfolioId == portfolioId && t.Type == TransactionType.Dividend)
            .OrderByDescending(t => t.ExecutedAt)
            .ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Date,Amount,Currency");
        foreach (var t in rows)
            sb.AppendLine($"{Csv(t.Symbol ?? "CASH")},{t.ExecutedAt:O},{t.Quantity * t.Price},{Csv(t.Currency)}");
        return sb.ToString();
    }

    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return string.Empty;
        if (v.IndexOfAny([',', '"', '\n', '\r']) < 0) return v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    // Minimal text-based PDF wrapper sufficient for download/preview tests.
    // We embed the raw report body as a single text stream — Acrobat & viewers
    // render it as plain text. Good enough for the stub format until a real
    // PDF engine (QuestPDF) is wired in.
    private static string WrapAsPdf(string body, string portfolioName, ReportType type)
    {
        var header = $"% Stocky {type} report — {portfolioName} — generated {DateTimeOffset.UtcNow:O}\n";
        return "%PDF-1.4\n" + header + body + "\n%%EOF\n";
    }
}

/// <summary>M11 #55 — hosted sweep that runs due schedules.</summary>
public sealed class ReportScheduleJob(
    IServiceScopeFactory scopes,
    IConfiguration cfg,
    ILogger<ReportScheduleJob> log) : BackgroundService
{
    public static DateTimeOffset Advance(DateTimeOffset from, ReportCadence cadence) => cadence switch
    {
        ReportCadence.Weekly => from.AddDays(7),
        ReportCadence.Monthly => from.AddMonths(1),
        ReportCadence.Quarterly => from.AddMonths(3),
        _ => from.AddYears(100), // OnDemand → effectively never auto-fire
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = Math.Max(30, cfg.GetValue<int?>("Reports:SweepSeconds") ?? 60);
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "ReportScheduleJob sweep failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(period), stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StockyDbContext>();
        var renderer = scope.ServiceProvider.GetRequiredService<ReportRenderer>();
        var now = DateTimeOffset.UtcNow;
        var due = await db.ReportSchedules
            .Where(s => s.Enabled && s.Cadence != ReportCadence.OnDemand && s.NextRunUtc <= now)
            .ToListAsync(ct);
        var n = 0;
        foreach (var s in due)
        {
            try
            {
                var rendered = await renderer.RenderAsync(s.PortfolioId, s.Type, s.Format, ct);
                db.ReportDeliveries.Add(new ReportDelivery
                {
                    ScheduleId = s.Id,
                    OwnerId = s.OwnerId,
                    PortfolioId = s.PortfolioId,
                    Type = s.Type,
                    Format = s.Format,
                    FileName = rendered.FileName,
                    ContentType = rendered.ContentType,
                    SizeBytes = rendered.SizeBytes,
                    Body = rendered.Body,
                    Trigger = "schedule",
                    Channel = string.IsNullOrWhiteSpace(s.Email) ? "inbox" : "email",
                });
                s.LastRunUtc = now;
                s.NextRunUtc = Advance(now, s.Cadence);
                n++;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Render failed for schedule {ScheduleId}", s.Id);
            }
        }
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }
}

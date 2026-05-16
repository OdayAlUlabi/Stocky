using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

// ===================== M11 #54 — Share tokens =====================

[ApiController]
[Authorize]
[Route("api/share-tokens")]
public class ShareTokensController(StockyDbContext db, ShareTokenService tokens, IHttpContextAccessor http) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShareTokenDto>>> List(CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var list = await db.ShareTokens.AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
        return Ok(list.Select(s => ToDto(s)));
    }

    [HttpPost]
    public async Task<ActionResult<ShareTokenDto>> Create([FromBody] CreateShareTokenRequest req, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        try
        {
            var st = await tokens.CreateAsync(ownerId, req, ct);
            return CreatedAtAction(nameof(List), null, ToDto(st));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var ok = await tokens.RevokeAsync(ownerId, id, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var st = await db.ShareTokens.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == ownerId, ct);
        if (st is null) return NotFound();
        db.ShareTokens.Remove(st);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private ShareTokenDto ToDto(ShareToken s)
    {
        var req = http.HttpContext?.Request;
        var baseUrl = req is null ? string.Empty : $"{req.Scheme}://{req.Host}";
        var url = $"{baseUrl}/share/{s.Token}";
        return new ShareTokenDto(
            s.Id, s.Token, s.PortfolioId, s.Label, s.CreatedAt, s.ExpiresAt, s.RevokedAt,
            s.ViewCount, s.LastViewedAt, s.IncludeTransactions, s.IncludeCostBasis,
            s.IsActive(DateTimeOffset.UtcNow), url);
    }
}

/// <summary>Anonymous read-only resolver for a shared portfolio link.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/share/{token}")]
public class PublicShareController(StockyDbContext db, ShareTokenService tokens) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SharedPortfolioDto>> Get(string token, CancellationToken ct)
    {
        var st = await tokens.ResolveAsync(token, ct);
        if (st is null) return NotFound(new { error = "Link not found, expired, or revoked." });

        var portfolio = await db.Portfolios.AsNoTracking().FirstOrDefaultAsync(p => p.Id == st.PortfolioId, ct);
        if (portfolio is null) return NotFound();

        var holdings = await db.Holdings.AsNoTracking()
            .Where(h => h.PortfolioId == st.PortfolioId)
            .OrderBy(h => h.Symbol)
            .ToListAsync(ct);

        var symbols = holdings.Select(h => h.Symbol).Distinct().ToList();
        var latest = await db.PriceQuotes.AsNoTracking()
            .Where(q => symbols.Contains(q.Symbol))
            .GroupBy(q => q.Symbol)
            .Select(g => g.OrderByDescending(q => q.AsOf).First())
            .ToListAsync(ct);
        var byPrice = latest.ToDictionary(q => q.Symbol, q => (decimal?)q.Price);

        var rows = holdings.Select(h =>
        {
            var price = byPrice.GetValueOrDefault(h.Symbol);
            var mv = price.HasValue ? (decimal?)(price.Value * h.Quantity) : null;
            decimal? unrealized = null;
            decimal? avg = null;
            if (st.IncludeCostBasis)
            {
                avg = h.AverageCost;
                if (mv.HasValue) unrealized = mv.Value - (h.Quantity * h.AverageCost);
            }
            return new SharedHoldingRowDto(h.Symbol, h.Quantity, price, mv, avg, unrealized);
        }).ToList();

        IReadOnlyList<SharedTransactionRowDto>? txRows = null;
        if (st.IncludeTransactions)
        {
            var tx = await db.Transactions.AsNoTracking()
                .Where(t => t.PortfolioId == st.PortfolioId)
                .OrderByDescending(t => t.ExecutedAt)
                .Take(100)
                .ToListAsync(ct);
            txRows = tx.Select(t => new SharedTransactionRowDto(
                t.ExecutedAt, t.Type.ToString(), t.Symbol, t.Quantity, t.Price)).ToList();
        }

        var totalMv = rows.Sum(r => r.MarketValue ?? 0m);
        decimal? totalUnreal = st.IncludeCostBasis ? rows.Sum(r => r.UnrealizedPnL ?? 0m) : null;

        return new SharedPortfolioDto(
            portfolio.Name, portfolio.BaseCurrency, DateTimeOffset.UtcNow,
            totalMv, totalUnreal, st.IncludeCostBasis, st.IncludeTransactions,
            rows, txRows);
    }
}

// ===================== M11 #55 — Scheduled exports =====================

[ApiController]
[Authorize]
[Route("api/report-schedules")]
public class ReportSchedulesController(StockyDbContext db, ReportRenderer renderer) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReportScheduleDto>>> List(CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var list = await db.ReportSchedules.AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
        return Ok(list.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<ReportScheduleDto>> Create([FromBody] CreateReportScheduleRequest req, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        if (!await db.Portfolios.AnyAsync(p => p.Id == req.PortfolioId && p.OwnerId == ownerId, ct))
            return NotFound(new { error = "Portfolio not found" });
        if (!TryParseType(req.Type, out var rtype)) return BadRequest(new { error = "Invalid Type" });
        if (!TryParseFormat(req.Format, out var fmt)) return BadRequest(new { error = "Invalid Format" });
        if (!TryParseCadence(req.Cadence, out var cad)) return BadRequest(new { error = "Invalid Cadence" });

        var now = DateTimeOffset.UtcNow;
        var s = new ReportSchedule
        {
            OwnerId = ownerId,
            PortfolioId = req.PortfolioId,
            Type = rtype,
            Format = fmt,
            Cadence = cad,
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email!.Trim(),
            Enabled = req.Enabled,
            CreatedAt = now,
            NextRunUtc = cad == ReportCadence.OnDemand ? now.AddYears(100) : ReportScheduleJob.Advance(now, cad),
        };
        db.ReportSchedules.Add(s);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), null, ToDto(s));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ReportScheduleDto>> Update(Guid id, [FromBody] UpdateReportScheduleRequest req, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var s = await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct);
        if (s is null) return NotFound();
        if (req.Type is not null)
        {
            if (!TryParseType(req.Type, out var t)) return BadRequest(new { error = "Invalid Type" });
            s.Type = t;
        }
        if (req.Format is not null)
        {
            if (!TryParseFormat(req.Format, out var f)) return BadRequest(new { error = "Invalid Format" });
            s.Format = f;
        }
        if (req.Cadence is not null)
        {
            if (!TryParseCadence(req.Cadence, out var c)) return BadRequest(new { error = "Invalid Cadence" });
            s.Cadence = c;
            s.NextRunUtc = c == ReportCadence.OnDemand
                ? DateTimeOffset.UtcNow.AddYears(100)
                : ReportScheduleJob.Advance(DateTimeOffset.UtcNow, c);
        }
        if (req.Email is not null) s.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        if (req.Enabled.HasValue) s.Enabled = req.Enabled.Value;
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var s = await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct);
        if (s is null) return NotFound();
        db.ReportSchedules.Remove(s);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Manual on-demand run — renders + stores a delivery and returns it.</summary>
    [HttpPost("{id:guid}/run")]
    public async Task<ActionResult<ReportDeliveryDto>> Run(Guid id, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var s = await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct);
        if (s is null) return NotFound();
        var rendered = await renderer.RenderAsync(s.PortfolioId, s.Type, s.Format, ct);
        var d = new ReportDelivery
        {
            ScheduleId = s.Id,
            OwnerId = ownerId,
            PortfolioId = s.PortfolioId,
            Type = s.Type,
            Format = s.Format,
            FileName = rendered.FileName,
            ContentType = rendered.ContentType,
            SizeBytes = rendered.SizeBytes,
            Body = rendered.Body,
            Trigger = "ondemand",
            Channel = "inbox",
        };
        db.ReportDeliveries.Add(d);
        s.LastRunUtc = DateTimeOffset.UtcNow;
        if (s.Cadence != ReportCadence.OnDemand)
            s.NextRunUtc = ReportScheduleJob.Advance(DateTimeOffset.UtcNow, s.Cadence);
        await db.SaveChangesAsync(ct);
        return new ReportDeliveryDto(d.Id, d.ScheduleId, d.PortfolioId, d.Type.ToString(), d.Format.ToString(),
            d.FileName, d.ContentType, d.SizeBytes, d.GeneratedAt, d.Trigger, d.Channel);
    }

    private static ReportScheduleDto ToDto(ReportSchedule s) => new(
        s.Id, s.PortfolioId, s.Type.ToString(), s.Format.ToString(), s.Cadence.ToString(),
        s.Email, s.Enabled, s.CreatedAt, s.NextRunUtc, s.LastRunUtc);

    private static bool TryParseType(string v, out ReportType t) => Enum.TryParse(v, true, out t);
    private static bool TryParseFormat(string v, out ReportFormat f) => Enum.TryParse(v, true, out f);
    private static bool TryParseCadence(string v, out ReportCadence c) => Enum.TryParse(v, true, out c);
}

[ApiController]
[Authorize]
[Route("api/report-deliveries")]
public class ReportDeliveriesController(StockyDbContext db, ReportRenderer renderer) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReportDeliveryDto>>> List([FromQuery] Guid? portfolioId, [FromQuery] Guid? scheduleId, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        IQueryable<ReportDelivery> q = db.ReportDeliveries.AsNoTracking().Where(d => d.OwnerId == ownerId);
        if (portfolioId.HasValue) q = q.Where(d => d.PortfolioId == portfolioId.Value);
        if (scheduleId.HasValue) q = q.Where(d => d.ScheduleId == scheduleId.Value);
        var list = await q.OrderByDescending(d => d.GeneratedAt).Take(200).ToListAsync(ct);
        return Ok(list.Select(d => new ReportDeliveryDto(
            d.Id, d.ScheduleId, d.PortfolioId, d.Type.ToString(), d.Format.ToString(),
            d.FileName, d.ContentType, d.SizeBytes, d.GeneratedAt, d.Trigger, d.Channel)));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var d = await db.ReportDeliveries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, ct);
        if (d is null) return NotFound();
        var bytes = System.Text.Encoding.UTF8.GetBytes(d.Body);
        return File(bytes, d.ContentType, d.FileName);
    }

    /// <summary>Ad-hoc one-shot render without a schedule (kept off the schedules table).</summary>
    [HttpPost("ondemand")]
    public async Task<ActionResult<ReportDeliveryDto>> OnDemand([FromQuery] Guid portfolioId, [FromQuery] string type, [FromQuery] string format, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        if (!await db.Portfolios.AnyAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct))
            return NotFound(new { error = "Portfolio not found" });
        if (!Enum.TryParse<ReportType>(type, true, out var rtype)) return BadRequest(new { error = "Invalid type" });
        if (!Enum.TryParse<ReportFormat>(format, true, out var fmt)) return BadRequest(new { error = "Invalid format" });

        var rendered = await renderer.RenderAsync(portfolioId, rtype, fmt, ct);
        var d = new ReportDelivery
        {
            OwnerId = ownerId,
            PortfolioId = portfolioId,
            Type = rtype,
            Format = fmt,
            FileName = rendered.FileName,
            ContentType = rendered.ContentType,
            SizeBytes = rendered.SizeBytes,
            Body = rendered.Body,
            Trigger = "ondemand",
            Channel = "inbox",
        };
        db.ReportDeliveries.Add(d);
        await db.SaveChangesAsync(ct);
        return new ReportDeliveryDto(d.Id, d.ScheduleId, d.PortfolioId, d.Type.ToString(), d.Format.ToString(),
            d.FileName, d.ContentType, d.SizeBytes, d.GeneratedAt, d.Trigger, d.Channel);
    }
}

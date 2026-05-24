using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

// =====================================================================
// M14 #114 — Cash management (deposits, withdrawals, fees, dividends)
// =====================================================================
[ApiController]
[Route("api/cash")]
public class CashController(StockyDbContext db, CashService cash, AuditLogger audit) : ControllerBase
{
    [HttpGet("transactions")]
    public async Task<ActionResult<IEnumerable<CashTransactionDto>>> List([FromQuery] Guid portfolioId)
    {
        var ownerId = User.GetOwnerId();
        var rows = await cash.ListAsync(portfolioId, ownerId);
        return rows.Select(ToDto).ToList();
    }

    [HttpGet("balances")]
    public async Task<ActionResult<IEnumerable<CashBalanceDto>>> Balances([FromQuery] Guid portfolioId)
    {
        var ownerId = User.GetOwnerId();
        var bal = await cash.BalancesAsync(portfolioId, ownerId);
        return bal.Select(b => new CashBalanceDto(portfolioId, b.Currency, b.Balance, b.Count)).ToList();
    }

    [HttpPost("transactions")]
    public async Task<ActionResult<CashTransactionDto>> Create([FromBody] CreateCashTransactionRequest body)
    {
        var ownerId = User.GetOwnerId();
        var owned = await db.Portfolios.AnyAsync(p => p.Id == body.PortfolioId && p.OwnerId == ownerId);
        if (!owned) return NotFound();

        TransactionType type;
        try { type = CashService.ParseType(body.Type); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }

        if (body.Amount <= 0) return BadRequest(new { error = "Amount must be positive; the sign is derived from type." });

        // store magnitude in Price; signed amount derived on read so the same row
        // round-trips through existing transaction tooling.
        var tx = new Transaction
        {
            PortfolioId = body.PortfolioId,
            Symbol = null,
            Type = type,
            Quantity = 1m,
            Price = Math.Abs(body.Amount),
            Fee = 0m,
            Currency = body.Currency,
            ExecutedAt = body.ExecutedAt ?? DateTimeOffset.UtcNow,
            Notes = body.Notes
        };
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();
        await audit.WriteAsync(ownerId, "create", "CashTransaction", tx.Id.ToString(), new { tx.PortfolioId, tx.Type, tx.Price, tx.Currency }, StatusCodes.Status201Created);

        return CreatedAtAction(nameof(List), new { portfolioId = body.PortfolioId }, ToDto(tx));
    }

    [HttpDelete("transactions/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid? portfolioId = null)
    {
        var ownerId = User.GetOwnerId();
        var tx = await db.Transactions.Include(t => t.Portfolio)
            .FirstOrDefaultAsync(t => t.Id == id && t.Portfolio.OwnerId == ownerId);
        if (tx is null) return NotFound();
        if (portfolioId is not null && tx.PortfolioId != portfolioId.Value) return NotFound();
        db.Transactions.Remove(tx);
        await db.SaveChangesAsync();
        await audit.WriteAsync(ownerId, "delete", "CashTransaction", id.ToString(), statusCode: StatusCodes.Status204NoContent);
        return NoContent();
    }

    private static CashTransactionDto ToDto(Transaction t) => new(
        t.Id,
        t.PortfolioId,
        t.Type.ToString(),
        CashService.SignedAmount(t.Type, t.Price),
        t.Currency,
        t.ExecutedAt,
        t.Notes);
}

// =====================================================================
// M14 #115 — Position notes & journaling
// =====================================================================
[ApiController]
[Route("api/position-notes")]
public class PositionNotesController(StockyDbContext db, AuditLogger audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PositionNoteDto>>> List([FromQuery] string? symbol, [FromQuery] Guid? portfolioId)
    {
        var ownerId = User.GetOwnerId();
        var q = db.PositionNotes.Where(n => n.OwnerId == ownerId);
        if (!string.IsNullOrWhiteSpace(symbol)) q = q.Where(n => n.Symbol == symbol.ToUpperInvariant());
        if (portfolioId is not null) q = q.Where(n => n.PortfolioId == portfolioId);
        var rows = await q.OrderByDescending(n => n.UpdatedAt).Take(500).ToListAsync();
        return rows.Select(ToDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<PositionNoteDto>> Create([FromBody] CreatePositionNoteRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Symbol)) return BadRequest(new { error = "Symbol required." });
        if (string.IsNullOrWhiteSpace(body.Body)) return BadRequest(new { error = "Body required." });
        var ownerId = User.GetOwnerId();
        if (body.PortfolioId is not null)
        {
            var owned = await db.Portfolios.AnyAsync(p => p.Id == body.PortfolioId && p.OwnerId == ownerId);
            if (!owned) return NotFound();
        }
        var note = new PositionNote
        {
            OwnerId = ownerId,
            PortfolioId = body.PortfolioId,
            Symbol = body.Symbol.ToUpperInvariant(),
            Body = body.Body.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.PositionNotes.Add(note);
        await db.SaveChangesAsync();
        await audit.WriteAsync(ownerId, "create", "PositionNote", note.Id.ToString(), new { note.Symbol }, StatusCodes.Status201Created);
        return CreatedAtAction(nameof(List), new { symbol = note.Symbol }, ToDto(note));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<PositionNoteDto>> Update(Guid id, [FromBody] UpdatePositionNoteRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Body)) return BadRequest(new { error = "Body required." });
        var ownerId = User.GetOwnerId();
        var note = await db.PositionNotes.FirstOrDefaultAsync(n => n.Id == id && n.OwnerId == ownerId);
        if (note is null) return NotFound();
        note.Body = body.Body.Trim();
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await audit.WriteAsync(ownerId, "update", "PositionNote", id.ToString());
        return ToDto(note);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var note = await db.PositionNotes.FirstOrDefaultAsync(n => n.Id == id && n.OwnerId == ownerId);
        if (note is null) return NotFound();
        db.PositionNotes.Remove(note);
        await db.SaveChangesAsync();
        await audit.WriteAsync(ownerId, "delete", "PositionNote", id.ToString());
        return NoContent();
    }

    private static PositionNoteDto ToDto(PositionNote n) => new(n.Id, n.PortfolioId, n.Symbol, n.Body, n.CreatedAt, n.UpdatedAt);
}

// =====================================================================
// M14 #112 — Audit log (read-only)
// =====================================================================
[ApiController]
[Route("api/audit")]
public class AuditController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuditEntryDto>>> List([FromQuery] int take = 200, [FromQuery] string? resource = null)
    {
        var ownerId = User.GetOwnerId();
        var q = db.AuditEntries.Where(a => a.OwnerId == ownerId);
        if (!string.IsNullOrWhiteSpace(resource)) q = q.Where(a => a.Resource == resource);
        var rows = await q.OrderByDescending(a => a.Timestamp).Take(Math.Clamp(take, 1, 1000)).ToListAsync();
        return rows.Select(a => new AuditEntryDto(a.Id, a.Timestamp, a.Action, a.Resource, a.ResourceId, a.Method, a.Path, a.StatusCode, a.ClientIp, a.Details)).ToList();
    }
}

// =====================================================================
// M14 #116 — Model portfolio templates
// =====================================================================
[ApiController]
[Route("api/model-templates")]
public class ModelTemplatesController(StockyDbContext db, AuditLogger audit) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<ModelPortfolioTemplateDto>> List()
    {
        return ModelPortfolioTemplates.All.Select(ToDto).ToList();
    }

    [HttpGet("{slug}")]
    public ActionResult<ModelPortfolioTemplateDto> Get(string slug)
    {
        var t = ModelPortfolioTemplates.FindBySlug(slug);
        return t is null ? NotFound() : ToDto(t);
    }

    [HttpPost("apply")]
    public async Task<ActionResult<PortfolioDto>> Apply([FromBody] ApplyTemplateRequest body)
    {
        var template = ModelPortfolioTemplates.FindBySlug(body.Slug);
        if (template is null) return NotFound(new { error = $"Unknown template '{body.Slug}'" });
        if (string.IsNullOrWhiteSpace(body.PortfolioName)) return BadRequest(new { error = "PortfolioName required." });

        var ownerId = User.GetOwnerId();

        var portfolio = new Portfolio
        {
            OwnerId = ownerId,
            Name = body.PortfolioName.Trim(),
            BaseCurrency = string.IsNullOrWhiteSpace(body.BaseCurrency) ? "USD" : body.BaseCurrency.ToUpperInvariant(),
            CreatedAt = DateTimeOffset.UtcNow,
            BenchmarkSymbol = "VOO"
        };
        db.Portfolios.Add(portfolio);

        foreach (var alloc in template.Allocations)
        {
            db.RebalanceTargets.Add(new RebalanceTarget
            {
                PortfolioId = portfolio.Id,
                Symbol = alloc.Symbol.ToUpperInvariant(),
                TargetWeightPercent = alloc.WeightPercent
            });
        }

        if (body.InitialCashDeposit is decimal cashAmt && cashAmt > 0m)
        {
            db.Transactions.Add(new Transaction
            {
                PortfolioId = portfolio.Id,
                Symbol = null,
                Type = TransactionType.Deposit,
                Quantity = 1m,
                Price = cashAmt,
                Fee = 0m,
                Currency = portfolio.BaseCurrency,
                ExecutedAt = DateTimeOffset.UtcNow,
                Notes = $"Initial deposit — applied template '{template.Slug}'"
            });
        }

        await db.SaveChangesAsync();
        await audit.WriteAsync(ownerId, "create", "Portfolio", portfolio.Id.ToString(), new { template = template.Slug, body.InitialCashDeposit }, StatusCodes.Status201Created);

        return CreatedAtAction("Get", "Portfolios", new { id = portfolio.Id },
            new PortfolioDto(portfolio.Id, portfolio.Name, portfolio.BaseCurrency, portfolio.CreatedAt, body.InitialCashDeposit ?? 0m, portfolio.CostBasisMethod.ToString()));
    }

    private static ModelPortfolioTemplateDto ToDto(ModelPortfolioTemplate t) => new(
        t.Slug, t.Name, t.Description, t.Risk,
        t.Allocations.Select(a => new ModelTemplateAllocationDto(a.Symbol, a.Name, a.AssetClass, a.WeightPercent)).ToList());
}

// =====================================================================
// M14 #111 — GDPR data export and account deletion
// =====================================================================
[ApiController]
[Route("api/account")]
public class AccountController(StockyDbContext db, AuditLogger audit) : ControllerBase
{
    [HttpGet("export")]
    public async Task<ActionResult<GdprExportDto>> Export()
    {
        var ownerId = User.GetOwnerId();

        var portfolios = await db.Portfolios.Where(p => p.OwnerId == ownerId).AsNoTracking().ToListAsync();
        var portfolioIds = portfolios.Select(p => p.Id).ToList();

        var holdings = await db.Holdings.Where(h => portfolioIds.Contains(h.PortfolioId)).AsNoTracking().ToListAsync();
        var transactions = await db.Transactions.Where(t => portfolioIds.Contains(t.PortfolioId)).AsNoTracking().ToListAsync();
        var watchlists = await db.Watchlists.Where(w => w.OwnerId == ownerId).Include(w => w.Items).AsNoTracking().ToListAsync();
        var alerts = await db.Alerts.Where(a => a.OwnerId == ownerId).AsNoTracking().ToListAsync();
        var notes = await db.PositionNotes.Where(n => n.OwnerId == ownerId).AsNoTracking().ToListAsync();
        var goals = await db.Goals.Where(g => g.OwnerId == ownerId).AsNoTracking().ToListAsync();
        var settings = await db.UserSettings.Where(s => s.OwnerId == ownerId).AsNoTracking().FirstOrDefaultAsync();

        await audit.WriteAsync(ownerId, "export", "Account", ownerId);

        return new GdprExportDto(
            ownerId,
            DateTimeOffset.UtcNow,
            portfolios,
            holdings,
            transactions,
            watchlists.Select(w => new { w.Id, w.Name, Items = w.Items.Select(i => new { i.Symbol, i.AddedAt }) }),
            alerts,
            notes,
            goals,
            settings is null ? new { } : (object)settings);
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string confirm = "")
    {
        if (!string.Equals(confirm, "delete", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Pass ?confirm=delete to confirm permanent account deletion." });

        var ownerId = User.GetOwnerId();

        var portfolios = await db.Portfolios.Where(p => p.OwnerId == ownerId).ToListAsync();
        var portfolioIds = portfolios.Select(p => p.Id).ToList();

        var transactions = db.Transactions.Where(t => portfolioIds.Contains(t.PortfolioId));
        var holdings = db.Holdings.Where(h => portfolioIds.Contains(h.PortfolioId));
        var taxLots = db.TaxLots.Where(l => portfolioIds.Contains(l.PortfolioId));
        var realized = db.RealizedGains.Where(r => portfolioIds.Contains(r.PortfolioId));
        var snapshots = db.PortfolioSnapshots.Where(s => portfolioIds.Contains(s.PortfolioId));
        var targets = db.RebalanceTargets.Where(t => portfolioIds.Contains(t.PortfolioId));
        var schedules = db.ReportSchedules.Where(s => s.OwnerId == ownerId);
        var deliveries = db.ReportDeliveries.Where(d => d.OwnerId == ownerId);
        var shares = db.ShareTokens.Where(s => s.OwnerId == ownerId);
        var alerts = db.Alerts.Where(a => a.OwnerId == ownerId);
        var alertEvents = db.AlertEvents.Where(a => a.OwnerId == ownerId);
        var watchlistItems = db.WatchlistItems.Where(i => db.Watchlists.Any(w => w.Id == i.WatchlistId && w.OwnerId == ownerId));
        var watchlists = db.Watchlists.Where(w => w.OwnerId == ownerId);
        var notes = db.PositionNotes.Where(n => n.OwnerId == ownerId);
        var goals = db.Goals.Where(g => g.OwnerId == ownerId);
        var settings = db.UserSettings.Where(s => s.OwnerId == ownerId);

        db.Transactions.RemoveRange(transactions);
        db.Holdings.RemoveRange(holdings);
        db.TaxLots.RemoveRange(taxLots);
        db.RealizedGains.RemoveRange(realized);
        db.PortfolioSnapshots.RemoveRange(snapshots);
        db.RebalanceTargets.RemoveRange(targets);
        db.ReportDeliveries.RemoveRange(deliveries);
        db.ReportSchedules.RemoveRange(schedules);
        db.ShareTokens.RemoveRange(shares);
        db.AlertEvents.RemoveRange(alertEvents);
        db.Alerts.RemoveRange(alerts);
        db.WatchlistItems.RemoveRange(watchlistItems);
        db.Watchlists.RemoveRange(watchlists);
        db.PositionNotes.RemoveRange(notes);
        db.Goals.RemoveRange(goals);
        db.UserSettings.RemoveRange(settings);
        db.Portfolios.RemoveRange(portfolios);

        await db.SaveChangesAsync();
        await audit.WriteAsync(ownerId, "delete", "Account", ownerId);
        // Audit row intentionally retained for compliance (legitimate-interest retention).
        return NoContent();
    }
}

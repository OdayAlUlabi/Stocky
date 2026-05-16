using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

// =====================================================================
// M14 #91 — User-facing key management (cookie/Entra-auth, NOT API-key auth)
// =====================================================================
[ApiController]
[Authorize]
[Route("api/api-keys")]
public class ApiKeysController(ApiKeyService keys, AuditLogger audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApiKeyDto>>> List()
    {
        var ownerId = User.GetOwnerId();
        var rows = await keys.ListAsync(ownerId);
        return rows.Select(ToDto).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<CreatedApiKeyDto>> Create([FromBody] CreateApiKeyRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name)) return BadRequest(new { error = "Name required." });
        var ownerId = User.GetOwnerId();
        var generated = await keys.GenerateAsync(ownerId, body.Name, body.Scopes ?? "read", body.ExpiresAt);
        await audit.WriteAsync(ownerId, "create", "ApiKey", generated.Record.Id.ToString(), new { generated.Record.Name, generated.Record.Prefix }, StatusCodes.Status201Created);
        return new CreatedApiKeyDto(ToDto(generated.Record), generated.Plaintext);
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var ok = await keys.RevokeAsync(id, ownerId);
        if (!ok) return NotFound();
        await audit.WriteAsync(ownerId, "revoke", "ApiKey", id.ToString());
        return NoContent();
    }

    internal static ApiKeyDto ToDto(Stocky.Api.Domain.ApiKey k) =>
        new(k.Id, k.Name, k.Prefix, k.Scopes, k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.LastUsedAt, k.IsActive);
}

// =====================================================================
// M14 #91 — Public REST API (API-key auth, read-only, rate-limited)
// =====================================================================
[ApiController]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
[EnableRateLimiting("api-key")]
[Route("v1/public")]
[Produces("application/json")]
public class PublicApiController(StockyDbContext db) : ControllerBase
{
    [HttpGet("portfolios")]
    public async Task<ActionResult<IEnumerable<PortfolioDto>>> Portfolios()
    {
        var ownerId = User.GetOwnerId();
        var list = await db.Portfolios
            .Where(p => p.OwnerId == ownerId)
            .Select(p => new PortfolioDto(p.Id, p.Name, p.BaseCurrency, p.CreatedAt, 0m, p.CostBasisMethod.ToString()))
            .ToListAsync();
        return list;
    }

    [HttpGet("portfolios/{id:guid}")]
    public async Task<ActionResult<PortfolioDto>> Portfolio(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var p = await db.Portfolios.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId);
        if (p is null) return NotFound();
        return new PortfolioDto(p.Id, p.Name, p.BaseCurrency, p.CreatedAt, 0m, p.CostBasisMethod.ToString());
    }

    [HttpGet("portfolios/{id:guid}/holdings")]
    public async Task<ActionResult<IEnumerable<HoldingDto>>> Holdings(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var owned = await db.Portfolios.AnyAsync(p => p.Id == id && p.OwnerId == ownerId);
        if (!owned) return NotFound();
        var rows = await db.Holdings.Where(h => h.PortfolioId == id)
            .Select(h => new HoldingDto(h.Id, h.Symbol, h.Quantity, h.AverageCost, null, null))
            .ToListAsync();
        return rows;
    }

    [HttpGet("portfolios/{id:guid}/transactions")]
    public async Task<ActionResult<IEnumerable<TransactionDto>>> Transactions(Guid id, [FromQuery] int take = 200)
    {
        var ownerId = User.GetOwnerId();
        var owned = await db.Portfolios.AnyAsync(p => p.Id == id && p.OwnerId == ownerId);
        if (!owned) return NotFound();
        var rows = await db.Transactions
            .Where(t => t.PortfolioId == id)
            .OrderByDescending(t => t.ExecutedAt)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(t => new TransactionDto(t.Id, t.Symbol, t.Type.ToString(), t.Quantity, t.Price, t.Fee, t.Currency, t.ExecutedAt, t.Notes))
            .ToListAsync();
        return rows;
    }
}

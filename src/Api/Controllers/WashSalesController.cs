using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// IRS §1091 wash-sale report. Companion to /capital-gains. Year defaults to UTC now.
/// </summary>
[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/wash-sales")]
public class WashSalesController(StockyDbContext db, WashSaleService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WashSaleReportDto>> Get(Guid portfolioId, [FromQuery] int? year, CancellationToken ct)
    {
        var ownerId = User.GetOwnerId();
        var exists = await db.Portfolios.AnyAsync(p => p.Id == portfolioId && p.OwnerId == ownerId, ct);
        if (!exists) return NotFound();

        var y = year ?? DateTime.UtcNow.Year;
        return await service.ComputeAsync(portfolioId, y, ct);
    }
}

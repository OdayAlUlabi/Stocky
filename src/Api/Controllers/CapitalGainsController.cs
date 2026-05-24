using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-021 capital-gains report. Computed from RealizedGain rows produced by
/// TaxLotService. Year filter defaults to the current calendar year.
/// </summary>
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/capital-gains")]
public class CapitalGainsController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CapitalGainsDto>> Get(Guid portfolioId, [FromQuery] int? year)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        var y = year ?? DateTime.UtcNow.Year;
        var start = new DateTimeOffset(new DateTime(y, 1, 1), TimeSpan.Zero);
        var end = start.AddYears(1);

        var gains = await db.RealizedGains
            .Where(g => g.PortfolioId == portfolioId && g.SoldAt >= start && g.SoldAt < end)
            .OrderByDescending(g => g.SoldAt)
            .ToListAsync();

        var shortTerm = gains.Where(g => !g.IsLongTerm).Sum(g => g.Gain);
        var longTerm = gains.Where(g => g.IsLongTerm).Sum(g => g.Gain);

        return new CapitalGainsDto(
            y, shortTerm, longTerm, shortTerm + longTerm,
            gains.Select(g => new RealizedGainDto(g.Id, g.Symbol, g.AcquiredAt, g.SoldAt, g.Quantity, g.CostBasis, g.Proceeds, g.Gain, g.IsLongTerm)).ToList());
    }
}

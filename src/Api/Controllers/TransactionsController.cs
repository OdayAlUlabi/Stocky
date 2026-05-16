using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/portfolios/{portfolioId:guid}/transactions")]
public class TransactionsController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TransactionDto>>> List(Guid portfolioId)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        var items = await db.Transactions
            .Where(t => t.PortfolioId == portfolioId)
            .OrderByDescending(t => t.ExecutedAt)
            .Select(t => new TransactionDto(t.Id, t.Symbol, t.Type.ToString(), t.Quantity, t.Price, t.Fee, t.Currency, t.ExecutedAt, t.Notes))
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<TransactionDto>> Create(Guid portfolioId, CreateTransactionRequest request)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();

        if (!Enum.TryParse<TransactionType>(request.Type, ignoreCase: true, out var type))
            return BadRequest($"Invalid transaction type '{request.Type}'.");

        var tx = new Transaction
        {
            PortfolioId = portfolioId,
            Symbol = request.Symbol,
            Type = type,
            Quantity = request.Quantity,
            Price = request.Price,
            Fee = request.Fee,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? portfolio.BaseCurrency : request.Currency,
            ExecutedAt = request.ExecutedAt == default ? DateTimeOffset.UtcNow : request.ExecutedAt,
            Notes = request.Notes
        };
        db.Transactions.Add(tx);

        if (!string.IsNullOrWhiteSpace(tx.Symbol) && (type == TransactionType.Buy || type == TransactionType.Sell))
        {
            await EnsureInstrumentAsync(tx.Symbol!, tx.Currency);
            var holding = portfolio.Holdings.FirstOrDefault(h => h.Symbol == tx.Symbol);
            if (holding is null)
            {
                holding = new Holding { PortfolioId = portfolioId, Symbol = tx.Symbol!, Quantity = 0, AverageCost = 0 };
                db.Holdings.Add(holding);
            }
            ApplyToHolding(holding, type, tx.Quantity, tx.Price);
        }

        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), new { portfolioId },
            new TransactionDto(tx.Id, tx.Symbol, tx.Type.ToString(), tx.Quantity, tx.Price, tx.Fee, tx.Currency, tx.ExecutedAt, tx.Notes));
    }

    private async Task EnsureInstrumentAsync(string symbol, string currency)
    {
        if (!await db.Instruments.AnyAsync(i => i.Symbol == symbol))
        {
            db.Instruments.Add(new Instrument { Symbol = symbol, Name = symbol, Exchange = "UNKNOWN", Currency = currency, AssetClass = "Equity" });
        }
    }

    private static void ApplyToHolding(Holding holding, TransactionType type, decimal qty, decimal price)
    {
        if (type == TransactionType.Buy)
        {
            var newQty = holding.Quantity + qty;
            holding.AverageCost = newQty == 0 ? 0 : ((holding.Quantity * holding.AverageCost) + (qty * price)) / newQty;
            holding.Quantity = newQty;
        }
        else if (type == TransactionType.Sell)
        {
            holding.Quantity -= qty;
            if (holding.Quantity <= 0)
            {
                holding.Quantity = 0;
                holding.AverageCost = 0;
            }
        }
    }
}

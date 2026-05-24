using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;
using Stocky.Api.Services;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-007 CSV import. Accepts a multipart form file with header row
/// "symbol,type,quantity,price,fee,currency,executedAt,notes".
/// </summary>
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/transactions/import")]
public class TransactionImportController(StockyDbContext db, TaxLotService taxLots, PortfolioLedgerService ledger) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(5_000_000)]
    public async Task<ActionResult<ImportResultDto>> Import(Guid portfolioId, IFormFile file)
    {
        var ownerId = User.GetOwnerId();
        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId && p.OwnerId == ownerId);
        if (portfolio is null) return NotFound();
        if (file is null || file.Length == 0) return BadRequest("File is required.");

        using var reader = new StreamReader(file.OpenReadStream());
        string? header = await reader.ReadLineAsync();
        if (header is null) return BadRequest("Empty file.");

        var headers = header.Split(',', StringSplitOptions.TrimEntries).Select(h => h.ToLowerInvariant()).ToArray();
        int idx(string name) => Array.IndexOf(headers, name);
        int iSym = idx("symbol"), iType = idx("type"), iQty = idx("quantity"),
            iPx = idx("price"), iFee = idx("fee"), iCur = idx("currency"),
            iExec = idx("executedat"), iNote = idx("notes");
        if (iSym < 0 || iType < 0 || iQty < 0 || iPx < 0 || iExec < 0)
            return BadRequest("CSV must contain columns: symbol, type, quantity, price, executedAt (fee/currency/notes optional).");

        int imported = 0, skipped = 0, line = 1;
        var errors = new List<string>();
        string? row;
        while ((row = await reader.ReadLineAsync()) is not null)
        {
            line++;
            if (string.IsNullOrWhiteSpace(row)) continue;
            var cells = SplitCsv(row);
            try
            {
                var typeStr = cells.ElementAtOrDefault(iType) ?? "";
                if (!Enum.TryParse<TransactionType>(typeStr, true, out var type))
                { skipped++; errors.Add($"line {line}: invalid type '{typeStr}'"); continue; }
                var symbol = cells.ElementAtOrDefault(iSym)?.Trim().ToUpperInvariant();
                if ((type == TransactionType.Buy || type == TransactionType.Sell) && string.IsNullOrWhiteSpace(symbol))
                { skipped++; errors.Add($"line {line}: symbol required"); continue; }
                var qty = decimal.Parse(cells[iQty], System.Globalization.CultureInfo.InvariantCulture);
                var price = decimal.Parse(cells[iPx], System.Globalization.CultureInfo.InvariantCulture);
                var fee = iFee >= 0 && cells.Count > iFee && !string.IsNullOrWhiteSpace(cells[iFee])
                    ? decimal.Parse(cells[iFee], System.Globalization.CultureInfo.InvariantCulture)
                    : 0m;
                var currency = iCur >= 0 && cells.Count > iCur && !string.IsNullOrWhiteSpace(cells[iCur])
                    ? cells[iCur].ToUpperInvariant() : portfolio.BaseCurrency;
                var executedAt = DateTimeOffset.Parse(cells[iExec], System.Globalization.CultureInfo.InvariantCulture);
                var notes = iNote >= 0 && cells.Count > iNote ? cells[iNote] : null;
                if (qty <= 0 || price < 0 || fee < 0 || executedAt > DateTimeOffset.UtcNow.AddMinutes(5))
                { skipped++; errors.Add($"line {line}: validation failed"); continue; }

                if (symbol is not null
                    && db.Instruments.Local.All(i => i.Symbol != symbol)
                    && !await db.Instruments.AnyAsync(i => i.Symbol == symbol))
                {
                    db.Instruments.Add(new Instrument { Symbol = symbol, Name = symbol, Exchange = "UNKNOWN", Currency = currency, AssetClass = "Equity" });
                }
                db.Transactions.Add(new Transaction
                {
                    PortfolioId = portfolioId,
                    Symbol = symbol,
                    Type = type,
                    Quantity = qty,
                    Price = price,
                    Fee = fee,
                    Currency = currency,
                    ExecutedAt = executedAt,
                    Notes = notes
                });
                imported++;
            }
            catch (Exception ex)
            {
                skipped++;
                errors.Add($"line {line}: {ex.Message}");
            }
        }

        if (imported == 0) return Ok(new ImportResultDto(0, skipped, errors));
        await db.SaveChangesAsync();
        await ledger.RecomputeHoldingsAsync(portfolioId);
        await db.SaveChangesAsync();
        await taxLots.RecomputeAsync(portfolioId);
        return Ok(new ImportResultDto(imported, skipped, errors));
    }

    private static List<string> SplitCsv(string row)
    {
        var cells = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < row.Length; i++)
        {
            var c = row[i];
            if (c == '"') { inQuote = !inQuote; continue; }
            if (c == ',' && !inQuote) { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }
}

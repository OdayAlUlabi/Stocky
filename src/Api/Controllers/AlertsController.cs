using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AlertsController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AlertDto>>> List([FromQuery] string? status)
    {
        var ownerId = User.GetOwnerId();
        var query = db.Alerts.Where(a => a.OwnerId == ownerId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AlertStatus>(status, true, out var s))
            query = query.Where(a => a.Status == s);
        var list = await query.OrderByDescending(a => a.CreatedAt)
            .Select(a => new AlertDto(a.Id, a.Symbol, a.Condition.ToString(), a.Threshold, a.Status.ToString(),
                a.CreatedAt, a.TriggeredAt, a.TriggeredValue, a.Note))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<AlertDto>> Create(CreateAlertRequest request)
    {
        if (!Enum.TryParse<AlertCondition>(request.Condition, true, out var cond))
            return BadRequest("Invalid condition.");
        if (string.IsNullOrWhiteSpace(request.Symbol)) return BadRequest("Symbol is required.");
        var alert = new Alert
        {
            OwnerId = User.GetOwnerId(),
            Symbol = request.Symbol.Trim().ToUpperInvariant(),
            Condition = cond,
            Threshold = request.Threshold,
            Note = request.Note,
            Status = AlertStatus.Active
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), null,
            new AlertDto(alert.Id, alert.Symbol, alert.Condition.ToString(), alert.Threshold, alert.Status.ToString(),
                alert.CreatedAt, alert.TriggeredAt, alert.TriggeredValue, alert.Note));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AlertDto>> Update(Guid id, UpdateAlertRequest request)
    {
        var ownerId = User.GetOwnerId();
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == ownerId);
        if (alert is null) return NotFound();
        if (!Enum.TryParse<AlertStatus>(request.Status, true, out var status))
            return BadRequest("Invalid status.");
        alert.Threshold = request.Threshold;
        alert.Status = status;
        alert.Note = request.Note;
        if (status == AlertStatus.Active)
        {
            alert.TriggeredAt = null;
            alert.TriggeredValue = null;
        }
        await db.SaveChangesAsync();
        return new AlertDto(alert.Id, alert.Symbol, alert.Condition.ToString(), alert.Threshold, alert.Status.ToString(),
            alert.CreatedAt, alert.TriggeredAt, alert.TriggeredValue, alert.Note);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == ownerId);
        if (alert is null) return NotFound();
        db.Alerts.Remove(alert);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

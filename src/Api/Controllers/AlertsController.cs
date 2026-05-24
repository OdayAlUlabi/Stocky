using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlertsController(StockyDbContext db) : ControllerBase
{
    private static AlertDto ToDto(Alert a) => new(
        a.Id, a.Symbol, a.Condition.ToString(), a.Threshold, a.Status.ToString(),
        a.CreatedAt, a.TriggeredAt, a.TriggeredValue, a.Note,
        a.Type.ToString(), a.IndicatorPeriod, a.KeywordFilter, a.MinSentiment,
        a.DaysBeforeEarnings, a.PortfolioId, a.Channels, a.WebhookUrl, a.SnoozedUntil);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AlertDto>>> List([FromQuery] string? status)
    {
        var ownerId = User.GetOwnerId();
        var query = db.Alerts.Where(a => a.OwnerId == ownerId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AlertStatus>(status, true, out var s))
            query = query.Where(a => a.Status == s);
        var rows = await query.OrderByDescending(a => a.CreatedAt).ToListAsync();
        return Ok(rows.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<AlertDto>> Create(CreateAlertRequest request)
    {
        if (!Enum.TryParse<AlertCondition>(request.Condition, true, out var cond))
            return BadRequest("Invalid condition.");
        if (string.IsNullOrWhiteSpace(request.Symbol)) return BadRequest("Symbol is required.");
        var type = AlertType.Price;
        if (!string.IsNullOrWhiteSpace(request.Type) && !Enum.TryParse(request.Type, true, out type))
            return BadRequest("Invalid alert type.");

        var alert = new Alert
        {
            OwnerId = User.GetOwnerId(),
            Symbol = request.Symbol.Trim().ToUpperInvariant(),
            Condition = cond,
            Threshold = request.Threshold,
            Note = request.Note,
            Status = AlertStatus.Active,
            Type = type,
            IndicatorPeriod = request.IndicatorPeriod,
            KeywordFilter = request.KeywordFilter,
            MinSentiment = request.MinSentiment,
            DaysBeforeEarnings = request.DaysBeforeEarnings,
            PortfolioId = request.PortfolioId,
            Channels = string.IsNullOrWhiteSpace(request.Channels) ? "Inbox" : request.Channels!,
            WebhookUrl = request.WebhookUrl
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), null, ToDto(alert));
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
        if (!string.IsNullOrWhiteSpace(request.Channels)) alert.Channels = request.Channels!;
        if (request.WebhookUrl is not null) alert.WebhookUrl = request.WebhookUrl;
        if (request.IndicatorPeriod is not null) alert.IndicatorPeriod = request.IndicatorPeriod;
        if (request.KeywordFilter is not null) alert.KeywordFilter = request.KeywordFilter;
        if (request.MinSentiment is not null) alert.MinSentiment = request.MinSentiment;
        if (request.DaysBeforeEarnings is not null) alert.DaysBeforeEarnings = request.DaysBeforeEarnings;
        if (status == AlertStatus.Active)
        {
            alert.TriggeredAt = null;
            alert.TriggeredValue = null;
            alert.SnoozedUntil = null;
        }
        await db.SaveChangesAsync();
        return ToDto(alert);
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

    /// <summary>M10 #53 — suppress an alert from re-firing until the supplied time.</summary>
    [HttpPost("{id:guid}/snooze")]
    public async Task<ActionResult<AlertDto>> Snooze(Guid id, SnoozeAlertRequest request)
    {
        var ownerId = User.GetOwnerId();
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == ownerId);
        if (alert is null) return NotFound();
        if (request.UntilUtc <= DateTimeOffset.UtcNow) return BadRequest("UntilUtc must be in the future.");
        alert.SnoozedUntil = request.UntilUtc;
        await db.SaveChangesAsync();
        return ToDto(alert);
    }

    /// <summary>Convenience reactivate (clear Triggered + Snoozed state).</summary>
    [HttpPost("{id:guid}/reactivate")]
    public async Task<ActionResult<AlertDto>> Reactivate(Guid id)
    {
        var ownerId = User.GetOwnerId();
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == ownerId);
        if (alert is null) return NotFound();
        alert.Status = AlertStatus.Active;
        alert.TriggeredAt = null;
        alert.TriggeredValue = null;
        alert.SnoozedUntil = null;
        await db.SaveChangesAsync();
        return ToDto(alert);
    }

    /// <summary>M10 #53 — past trip history for the signed-in user (most recent first).</summary>
    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<AlertEventDto>>> History([FromQuery] int take = 200)
    {
        var ownerId = User.GetOwnerId();
        take = Math.Clamp(take, 1, 1000);
        var rows = await db.AlertEvents
            .Where(e => e.OwnerId == ownerId)
            .OrderByDescending(e => e.TriggeredAt)
            .Take(take)
            .Select(e => new AlertEventDto(
                e.Id, e.AlertId, e.Symbol, e.Type.ToString(), e.Condition.ToString(),
                e.TriggeredAt, e.TriggeredValue, e.Message, e.Channels, e.Context))
            .ToListAsync();
        return Ok(rows);
    }
}

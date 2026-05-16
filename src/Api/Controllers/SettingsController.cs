using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Domain;
using Stocky.Api.Dtos;

namespace Stocky.Api.Controllers;

/// <summary>
/// SCR-040 user settings. Lazily creates a default row on first read.
/// </summary>
[ApiController]
[Authorize]
[Route("api/settings")]
public class SettingsController(StockyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserSettingsDto>> Get()
    {
        var ownerId = User.GetOwnerId();
        var s = await db.UserSettings.FirstOrDefaultAsync(x => x.OwnerId == ownerId);
        if (s is null)
        {
            s = new UserSettings { OwnerId = ownerId };
            db.UserSettings.Add(s);
            await db.SaveChangesAsync();
        }
        return new UserSettingsDto(s.DisplayCurrency, s.Theme, s.Locale, s.EmailAlerts, s.WeeklyDigest);
    }

    [HttpPut]
    public async Task<ActionResult<UserSettingsDto>> Put(UserSettingsDto dto)
    {
        var ownerId = User.GetOwnerId();
        var s = await db.UserSettings.FirstOrDefaultAsync(x => x.OwnerId == ownerId);
        if (s is null)
        {
            s = new UserSettings { OwnerId = ownerId };
            db.UserSettings.Add(s);
        }
        s.DisplayCurrency = string.IsNullOrWhiteSpace(dto.DisplayCurrency) ? "USD" : dto.DisplayCurrency.ToUpperInvariant();
        s.Theme = dto.Theme;
        s.Locale = dto.Locale;
        s.EmailAlerts = dto.EmailAlerts;
        s.WeeklyDigest = dto.WeeklyDigest;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return new UserSettingsDto(s.DisplayCurrency, s.Theme, s.Locale, s.EmailAlerts, s.WeeklyDigest);
    }
}

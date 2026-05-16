using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Stocky.Api.Data;
using Stocky.Api.Dtos;

namespace Stocky.Api.Services;

/// <summary>
/// M8 #1 — Real-time quote streaming hub at /hubs/prices. Clients call
/// <c>Subscribe(symbols)</c> to join symbol-named groups; QuoteRefresher
/// broadcasts <c>"price"</c> ticks to subscribers each refresh cycle.
/// </summary>
[Authorize]
public sealed class PricesHub : Hub
{
    public async Task Subscribe(string[] symbols)
    {
        if (symbols is null) return;
        foreach (var s in symbols)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            await Groups.AddToGroupAsync(Context.ConnectionId, s.ToUpperInvariant());
        }
    }

    public async Task Unsubscribe(string[] symbols)
    {
        if (symbols is null) return;
        foreach (var s in symbols)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, s.ToUpperInvariant());
        }
    }

    // M14 #92 — per-portfolio streaming. Clients join "portfolio:{id}" groups
    // and receive a "portfolioUpdated" event whenever a relevant ledger or
    // quote change touches the portfolio.
    public async Task SubscribePortfolio(string portfolioId, [Microsoft.AspNetCore.Mvc.FromServices] StockyDbContext db)
    {
        if (!Guid.TryParse(portfolioId, out var pid)) return;
        var ownerId = Context.User?.GetOwnerId();
        if (string.IsNullOrWhiteSpace(ownerId)) return;
        var owned = await db.Portfolios.AnyAsync(p => p.Id == pid && p.OwnerId == ownerId);
        if (!owned) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"portfolio:{pid}");
    }

    public Task UnsubscribePortfolio(string portfolioId)
    {
        if (!Guid.TryParse(portfolioId, out var pid)) return Task.CompletedTask;
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"portfolio:{pid}");
    }
}

/// <summary>
/// M14 #92 — Fans-out a lightweight "portfolioUpdated" event to all clients
/// currently subscribed to a portfolio's group. Called by the ledger service
/// after transactions and by QuoteRefresher after relevant ticks.
/// </summary>
public sealed class PortfolioUpdatedBroadcaster(IHubContext<PricesHub> hub)
{
    public Task BroadcastAsync(Guid portfolioId, string reason, CancellationToken ct = default)
    {
        var payload = new { portfolioId, reason, at = DateTimeOffset.UtcNow };
        return hub.Clients.Group($"portfolio:{portfolioId}").SendAsync("portfolioUpdated", payload, ct);
    }
}

/// <summary>
/// Helper that pushes price ticks to symbol groups. Injected into
/// QuoteRefresher so each refresh fan-outs through the hub.
/// </summary>
public sealed class PriceTickBroadcaster(IHubContext<PricesHub> hub)
{
    public Task BroadcastAsync(IEnumerable<QuoteDto> quotes, CancellationToken ct = default)
    {
        var tasks = new List<Task>();
        foreach (var q in quotes)
        {
            var tick = new PriceTickDto(q.Symbol, q.Price, q.Change, q.ChangePercent, q.AsOf);
            tasks.Add(hub.Clients.Group(q.Symbol).SendAsync("price", tick, ct));
        }
        return Task.WhenAll(tasks);
    }
}

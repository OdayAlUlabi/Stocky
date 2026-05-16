using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Stocky.Api.Data;

namespace Stocky.Api.Services;

/// <summary>
/// Tags the current Activity with custom dimensions so traces in Application
/// Insights carry the owner (hashed for privacy), portfolio id, and symbol
/// alongside the request. Tags become customDimensions on requests/dependencies.
/// </summary>
public sealed class TelemetryEnricherMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext ctx)
    {
        var activity = Activity.Current;
        if (activity is not null)
        {
            // OwnerId — hashed so we never write the raw oid into telemetry.
            try
            {
                var ownerId = ctx.User.GetOwnerId();
                if (!string.IsNullOrEmpty(ownerId))
                {
                    activity.SetTag("stocky.owner_hash", HashOwner(ownerId));
                }
            }
            catch
            {
                // GetOwnerId throws when the user is unauthenticated; ignore for anonymous routes.
            }

            // PortfolioId — pulled from the route when present.
            if (ctx.Request.RouteValues.TryGetValue("portfolioId", out var pid) && pid is not null)
            {
                activity.SetTag("stocky.portfolio_id", pid.ToString());
            }
            else if (ctx.Request.RouteValues.TryGetValue("id", out var id) && id is not null
                && ctx.Request.Path.StartsWithSegments("/api/portfolios"))
            {
                activity.SetTag("stocky.portfolio_id", id.ToString());
            }

            // Symbol — from query string when the controller filters by symbol.
            if (ctx.Request.Query.TryGetValue("symbol", out var symbol) && !string.IsNullOrWhiteSpace(symbol))
            {
                activity.SetTag("stocky.symbol", symbol.ToString()!.ToUpperInvariant());
            }
        }

        await next(ctx);
    }

    private static string HashOwner(string ownerId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(ownerId), hash);
        // 12 hex chars is enough to correlate without becoming a re-identification risk.
        return Convert.ToHexString(hash[..6]);
    }
}

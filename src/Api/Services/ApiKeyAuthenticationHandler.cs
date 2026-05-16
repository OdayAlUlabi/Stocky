using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Stocky.Api.Services;

/// <summary>
/// M14 #91 — Bearer authentication for user-issued API keys.
/// Recognizes <c>Authorization: Bearer sk_...</c> and <c>X-API-Key: sk_...</c>.
/// Issues a ClaimsPrincipal with <c>oid</c> set to ApiKey.OwnerId so all
/// downstream code (User.GetOwnerId()) works unchanged.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceProvider sp)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = ExtractKey(Request);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return AuthenticateResult.NoResult();
        }

        // Resolve the scoped service lazily — the handler itself is registered
        // by the authentication framework outside the request scope.
        using var scope = sp.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var key = await svc.ValidateAsync(raw);
        if (key is null)
        {
            return AuthenticateResult.Fail("Invalid or revoked API key.");
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("oid", key.OwnerId),
            new Claim("apikey_id", key.Id.ToString()),
            new Claim("scope", key.Scopes),
            new Claim(ClaimTypes.Name, $"apikey:{key.Prefix}")
        }, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    private static string? ExtractKey(HttpRequest req)
    {
        if (req.Headers.TryGetValue("X-API-Key", out var x) && !string.IsNullOrWhiteSpace(x))
            return x.ToString();
        if (req.Headers.TryGetValue("Authorization", out var auth))
        {
            var value = auth.ToString();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = value.Substring("Bearer ".Length).Trim();
                if (token.StartsWith("sk_", StringComparison.Ordinal))
                    return token;
            }
        }
        return null;
    }
}

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Stocky.Web.Mvc.Internal;

/// <summary>
/// Single-user passthrough authentication. Every request is signed in as a
/// fixed local identity so <c>[Authorize]</c> attributes keep working and
/// owner-scoped queries (<c>User.GetOwnerId()</c>) resolve a stable <c>sub</c>.
///
/// Override the owner id with <c>Auth:LocalOwnerId</c> to keep existing rows visible.
/// </summary>
public sealed class AutoAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Auto";

    private readonly string _ownerId;
    private readonly string _displayName;

    public AutoAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _ownerId = configuration["Auth:LocalOwnerId"]
            ?? Environment.GetEnvironmentVariable("AUTH__LOCALOWNERID")
            ?? "local-user";
        _displayName = configuration["Auth:LocalDisplayName"] ?? "Local User";
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("sub", _ownerId),
            new Claim(ClaimTypes.NameIdentifier, _ownerId),
            new Claim(ClaimTypes.Name, _displayName),
        };
        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

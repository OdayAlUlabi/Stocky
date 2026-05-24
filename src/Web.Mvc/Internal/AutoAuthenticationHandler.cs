using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Stocky.Web.Mvc.Internal;

/// <summary>
/// Single-user passthrough authentication. After Google OAuth was removed,
/// every request is signed in as a fixed local identity so existing
/// <c>[Authorize]</c> attributes keep working and owner-scoped queries
/// (<c>User.GetOwnerId()</c>) continue to resolve a stable <c>sub</c>.
///
/// The owner id can be overridden with <c>Auth:LocalOwnerId</c> so existing
/// rows owned by a previous Google <c>sub</c> remain visible.
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

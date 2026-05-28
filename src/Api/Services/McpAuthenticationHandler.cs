using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Stocky.Api.Services;

/// <summary>
/// Service-account authentication for the Stocky MCP server.
/// Recognises the <c>X-Mcp-Service-Key</c> request header and, when it matches
/// the value in <c>Mcp:ServiceKey</c> configuration, synthesises a
/// <see cref="ClaimsPrincipal"/> whose <c>sub</c> claim equals <c>Mcp:OwnerId</c>.
/// This lets the MCP server call every API endpoint as the configured owner
/// without needing a Google OAuth token.
/// </summary>
public sealed class McpAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration config)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "MCP";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Mcp-Service-Key", out var keyHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var configuredKey = config["Mcp:ServiceKey"];
        if (string.IsNullOrWhiteSpace(configuredKey) || keyHeader.ToString() != configuredKey)
            return Task.FromResult(AuthenticateResult.Fail("Invalid MCP service key."));

        var ownerId = config["Mcp:OwnerId"];
        if (string.IsNullOrWhiteSpace(ownerId))
            return Task.FromResult(AuthenticateResult.Fail("Mcp:OwnerId is not configured on the API."));

        var identity = new ClaimsIdentity([
            new Claim("sub", ownerId),
            new Claim(ClaimTypes.Name, "mcp-service")
        ], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

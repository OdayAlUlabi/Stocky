using Stocky.Mcp.Tools;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

// ── Stocky API HTTP client ────────────────────────────────────────────────────
var apiSection = builder.Configuration.GetSection("StockyApi");
builder.Services.AddHttpClient("StockyApi", client =>
{
    client.BaseAddress = new Uri(apiSection["BaseUrl"]
        ?? throw new InvalidOperationException("StockyApi:BaseUrl is required"));
    var serviceKey = apiSection["ServiceKey"];
    if (!string.IsNullOrWhiteSpace(serviceKey))
        client.DefaultRequestHeaders.Add("X-Mcp-Service-Key", serviceKey);
});

// ── MCP server (Streamable HTTP transport) ────────────────────────────────────
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(PortfolioTools).Assembly);

var app = builder.Build();

var rawBaseUrl = app.Configuration["App:BaseUrl"];
var baseUrl = (string.IsNullOrEmpty(rawBaseUrl)
               ? "https://stocky.swedencentral.cloudapp.azure.com"
               : rawBaseUrl).TrimEnd('/');

// In-memory pending authorization codes: code → (redirectUri, codeChallenge, expiry)
var pendingCodes = new ConcurrentDictionary<string,
    (string RedirectUri, string CodeChallenge, DateTime Expiry)>();

// ── OAuth discovery (RFC 8414) ────────────────────────────────────────────────
app.MapGet("/.well-known/oauth-authorization-server", () => Results.Json(new
{
    issuer                              = baseUrl,
    authorization_endpoint              = $"{baseUrl}/authorize",
    token_endpoint                      = $"{baseUrl}/token",
    registration_endpoint               = $"{baseUrl}/register",
    response_types_supported            = new[] { "code" },
    grant_types_supported               = new[] { "authorization_code" },
    code_challenge_methods_supported    = new[] { "S256" }
}));

// ── Dynamic Client Registration (RFC 7591) ───────────────────────────────────
// Claude.ai calls this before starting the auth flow. We issue a client_id
// on the spot — no secret needed for PKCE flows.
app.MapPost("/register", async (HttpRequest req) =>
{
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body);
    var root = doc.RootElement;

    var clientId   = Guid.NewGuid().ToString("N");
    var clientName = root.TryGetProperty("client_name", out var n) ? n.GetString() : "mcp-client";
    var redirects  = root.TryGetProperty("redirect_uris", out var u)
                     ? u.EnumerateArray().Select(x => x.GetString()).ToArray()
                     : Array.Empty<string>();

    return Results.Json(new
    {
        client_id                  = clientId,
        client_name                = clientName,
        redirect_uris              = redirects,
        token_endpoint_auth_method = "none",
        grant_types                = new[] { "authorization_code" },
        response_types             = new[] { "code" }
    }, statusCode: 201);
});

// ── Authorization — GET: show login form ──────────────────────────────────────
app.MapGet("/authorize", (HttpRequest req) =>
{
    var html = $$"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>Connect Stocky to Claude</title>
        <style>
          body  { font-family: system-ui, sans-serif; max-width: 380px; margin: 80px auto; padding: 20px; }
          h2   { margin-bottom: 4px; }
          p    { color: #555; font-size: 14px; margin-bottom: 16px; }
          input[type=password] { width: 100%; padding: 10px; margin-bottom: 14px;
                                 box-sizing: border-box; border: 1px solid #ccc; border-radius: 4px; }
          button { background: #0066cc; color: white; padding: 10px; width: 100%;
                   border: none; border-radius: 4px; cursor: pointer; font-size: 16px; }
        </style></head>
        <body>
          <h2>Connect Stocky to Claude</h2>
          <p>Enter the Stocky MCP access key to allow Claude to access your portfolio data.</p>
          <form method="post">
            <input type="hidden" name="redirect_uri"   value="{{HtmlEncoder.Default.Encode(req.Query["redirect_uri"].ToString())}}" />
            <input type="hidden" name="state"          value="{{HtmlEncoder.Default.Encode(req.Query["state"].ToString())}}" />
            <input type="hidden" name="client_id"      value="{{HtmlEncoder.Default.Encode(req.Query["client_id"].ToString())}}" />
            <input type="hidden" name="code_challenge" value="{{HtmlEncoder.Default.Encode(req.Query["code_challenge"].ToString())}}" />
            <input type="password" name="access_key" placeholder="MCP access key" autofocus />
            <button type="submit">Connect</button>
          </form>
        </body></html>
        """;
    return Results.Content(html, "text/html");
});

// ── Authorization — POST: validate key, issue code ────────────────────────────
app.MapPost("/authorize", async (HttpRequest req) =>
{
    var form          = await req.ReadFormAsync();
    var accessKey     = form["access_key"].ToString();
    var redirectUri   = form["redirect_uri"].ToString();
    var state         = form["state"].ToString();
    var codeChallenge = form["code_challenge"].ToString();

    // Constant-time compare via HMAC to mitigate timing attacks
    var expected = app.Configuration["StockyApi:ServiceKey"] ?? "";
    var tag      = "stocky-oauth"u8.ToArray();
    var isValid  = !string.IsNullOrEmpty(expected) && CryptographicOperations.FixedTimeEquals(
                       HMACSHA256.HashData(tag, Encoding.UTF8.GetBytes(expected)),
                       HMACSHA256.HashData(tag, Encoding.UTF8.GetBytes(accessKey)));

    if (!isValid)
        return Results.Content("""
            <!DOCTYPE html><html>
            <body style="font-family:system-ui;max-width:380px;margin:80px auto;padding:20px">
            <p>Invalid access key. <a href="javascript:history.back()">Go back and try again.</a></p>
            </body></html>
            """, "text/html");

    var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
               .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    pendingCodes[code] = (redirectUri, codeChallenge, DateTime.UtcNow.AddMinutes(5));

    var sep = redirectUri.Contains('?') ? '&' : '?';
    return Results.Redirect(
        $"{redirectUri}{sep}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
});

// ── Token endpoint: exchange code → bearer token ──────────────────────────────
app.MapPost("/token", async (HttpRequest req) =>
{
    var form         = await req.ReadFormAsync();
    var code         = form["code"].ToString();
    var codeVerifier = form["code_verifier"].ToString();

    if (!pendingCodes.TryRemove(code, out var stored) || stored.Expiry < DateTime.UtcNow)
        return Results.Json(new { error = "invalid_grant" }, statusCode: 400);

    // PKCE S256 verification
    if (!string.IsNullOrEmpty(stored.CodeChallenge))
    {
        var computed = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)))
                       .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computed),
                Encoding.UTF8.GetBytes(stored.CodeChallenge)))
            return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
    }

    var secret = app.Configuration["StockyApi:ServiceKey"] ?? "";
    return Results.Json(new
    {
        access_token = IssueToken(secret),
        token_type   = "Bearer",
        expires_in   = 2_592_000  // 30 days
    });
});

// ── Bearer auth middleware — protects /mcp ────────────────────────────────────
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/mcp"))
    {
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers.WWWAuthenticate =
                $"Bearer realm=\"Stocky MCP\", " +
                $"resource_metadata=\"{baseUrl}/.well-known/oauth-authorization-server\"";
            return;
        }
        var token  = auth["Bearer ".Length..].Trim();
        var secret = ctx.RequestServices.GetRequiredService<IConfiguration>()["StockyApi:ServiceKey"] ?? "";
        if (!ValidateToken(token, secret))
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers.WWWAuthenticate =
                $"Bearer error=\"invalid_token\", " +
                $"resource_metadata=\"{baseUrl}/.well-known/oauth-authorization-server\"";
            return;
        }
    }
    await next(ctx);
});

// Health probe (no auth required — used by App Gateway probe and ACA liveness)
app.MapGet("/", () => Results.Ok(new { status = "ok", server = "Stocky MCP" }));

// MCP endpoint — Claude.ai connects here (protected by Bearer middleware above)
app.MapMcp("/mcp");

app.Run();

// ── Token helpers (HMAC-SHA256, stateless — survive restarts) ─────────────────

static string IssueToken(string secret)
{
    var expiry  = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
    var nonce   = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    var payload = $"{expiry}:{nonce}";
    var sig     = Convert.ToBase64String(
                      HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)))
                  .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    return $"{payload}:{sig}";
}

static bool ValidateToken(string token, string secret)
{
    var parts = token.Split(':');
    if (parts.Length != 3) return false;
    if (!long.TryParse(parts[0], out var expiry)) return false;
    if (DateTimeOffset.FromUnixTimeSeconds(expiry) < DateTimeOffset.UtcNow) return false;
    var payload  = $"{parts[0]}:{parts[1]}";
    var expected = Convert.ToBase64String(
                       HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)))
                   .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(expected),
        Encoding.UTF8.GetBytes(parts[2]));
}

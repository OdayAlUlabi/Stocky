using System.Net.Http.Headers;
using Stocky.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// ── Stocky API HTTP client ────────────────────────────────────────────────────
var apiSection = builder.Configuration.GetSection("StockyApi");
builder.Services.AddHttpClient("StockyApi", client =>
{
    client.BaseAddress = new Uri(apiSection["BaseUrl"]
        ?? throw new InvalidOperationException("StockyApi:BaseUrl is required"));
    var token = apiSection["Token"];
    if (!string.IsNullOrWhiteSpace(token))
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
});

// ── MCP server (Streamable HTTP transport) ────────────────────────────────────
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(PortfolioTools).Assembly);

var app = builder.Build();

// Basic health probe so Claude.ai can verify the server is alive.
app.MapGet("/", () => Results.Ok(new { status = "ok", server = "Stocky MCP" }));

// MCP endpoint — Claude.ai connects here.
app.MapMcp("/mcp");

app.Run();

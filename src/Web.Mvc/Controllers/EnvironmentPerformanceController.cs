using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stocky.Web.Mvc.ViewModels;

namespace Stocky.Web.Mvc.Controllers;

[Authorize]
public class EnvironmentPerformanceController : Controller
{
    private static readonly DateTime ProcessStartUtc = DateTime.UtcNow;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly IConfiguration _configuration;

    public EnvironmentPerformanceController(
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment hostEnvironment,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _hostEnvironment = hostEnvironment;
        _configuration = configuration;
    }

    [HttpGet("EnvironmentPerformance")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = await BuildModelAsync(ct);
        return View(model);
    }

    [HttpGet("EnvironmentPerformance/Status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var model = await BuildModelAsync(ct);
        return Json(model);
    }

    private async Task<EnvironmentPerformanceViewModel> BuildModelAsync(CancellationToken ct)
    {
        var services = new List<(string Name, string Url)>
        {
            ("API", _configuration["Performance:ApiUrl"] ?? "https://ca-stocky-prod-api.delightfulflower-1900046d.swedencentral.azurecontainerapps.io/"),
            ("Web MVC", _configuration["Performance:WebUrl"] ?? "https://stocky.swedencentral.cloudapp.azure.com/"),
            ("MCP", _configuration["Performance:McpUrl"] ?? "https://ca-stocky-prod-mcp.delightfulflower-1900046d.swedencentral.azurecontainerapps.io/")
        };

        var rows = new List<ServicePerformanceRow>(services.Count);
        foreach (var svc in services)
        {
            rows.Add(await ProbeServiceAsync(svc.Name, svc.Url, ct));
        }

        var proc = Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - ProcessStartUtc;

        return new EnvironmentPerformanceViewModel
        {
            EnvironmentName = _hostEnvironment.EnvironmentName,
            MachineName = Environment.MachineName,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Uptime = $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s",
            WorkingSetMb = Math.Round(proc.WorkingSet64 / 1024d / 1024d, 1),
            GcHeapMb = Math.Round(GC.GetTotalMemory(forceFullCollection: false) / 1024d / 1024d, 1),
            ThreadCount = proc.Threads.Count,
            HealthyServiceCount = rows.Count(r => r.IsHealthy),
            TotalServiceCount = rows.Count,
            Services = rows
        };
    }

    private async Task<ServicePerformanceRow> ProbeServiceAsync(string name, string url, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(8);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Stocky-WebMvc-EnvPerf/1.0");

        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            var statusCode = (int)response.StatusCode;
            var healthy = statusCode is >= 200 and < 400;

            return new ServicePerformanceRow
            {
                Name = name,
                Url = url,
                IsHealthy = healthy,
                StatusCode = statusCode,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                Details = healthy ? "Responding" : $"HTTP {statusCode}"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ServicePerformanceRow
            {
                Name = name,
                Url = url,
                IsHealthy = false,
                StatusCode = 0,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                Details = ex.Message
            };
        }
    }
}

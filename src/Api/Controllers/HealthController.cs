using Microsoft.AspNetCore.Mvc;

namespace Stocky.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", utc = DateTimeOffset.UtcNow });
}

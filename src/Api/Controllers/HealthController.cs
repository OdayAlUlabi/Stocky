using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Stocky.Api.Controllers;

[ApiController]
[Route("[controller]")]
[AllowAnonymous] // Container/App Gateway probes hit this without a bearer token.
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", utc = DateTimeOffset.UtcNow });
}

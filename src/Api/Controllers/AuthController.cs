using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Stocky.Api.Controllers;

/// <summary>
/// Diagnostic endpoints for verifying that a Bearer token reached the API and was accepted.
/// Anonymous-safe: returns <c>authenticated=false</c> for unauthenticated callers instead of 401.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        var user = HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return Ok(new { authenticated = false });
        }

        var claims = user.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray());

        return Ok(new
        {
            authenticated = true,
            name = user.Identity?.Name,
            authenticationType = user.Identity?.AuthenticationType,
            claims
        });
    }
}

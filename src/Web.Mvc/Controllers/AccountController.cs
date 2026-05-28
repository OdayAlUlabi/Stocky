using Microsoft.AspNetCore.Mvc;

namespace Stocky.Web.Mvc.Controllers;

// AutoAuthenticationHandler signs every request in as a fixed local user.
// /Account/Login is a no-op redirect kept only for any residual LoginPath references.
public class AccountController : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
        => LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);

    [HttpGet]
    public IActionResult Logout() => RedirectToAction(nameof(SignedOut));

    [HttpGet]
    public IActionResult SignedOut() => View();
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Stocky.Web.Mvc.Internal;

/// <summary>
/// Invokes an existing <c>Stocky.Api</c> REST controller in-process from an MVC
/// controller and returns its raw payload. This is the core pattern that lets
/// the new HTML host re-use the API's read endpoints without duplicating the
/// EF queries that live inside each <c>[ApiController]</c>.
///
/// The MVC controller's <see cref="HttpContext"/> (with cookie-auth User) is
/// forwarded, so <c>User.GetOwnerId()</c> resolves to the same owner the SPA
/// would have used with an id_token.
/// </summary>
public static class ApiControllerInvoker
{
    /// <summary>
    /// Resolves an <c>Stocky.Api</c> controller via DI, attaches the current
    /// MVC controller's <see cref="HttpContext"/>, invokes <paramref name="action"/>,
    /// and unwraps the resulting <see cref="ActionResult{TValue}"/>.
    /// </summary>
    /// <returns>
    /// The payload, or <c>default</c> if the API controller returned NotFound /
    /// any non-OK <see cref="IActionResult"/>.
    /// </returns>
    public static async Task<TValue?> InvokeAsync<TController, TValue>(
        this Controller mvc,
        Func<TController, Task<ActionResult<TValue>>> action)
        where TController : ControllerBase
    {
        var ctrl = ActivatorUtilities.CreateInstance<TController>(mvc.HttpContext.RequestServices);
        ctrl.ControllerContext = new ControllerContext { HttpContext = mvc.HttpContext };
        var result = await action(ctrl);
        return UnwrapValue(result);
    }

    /// <summary>
    /// Invokes a synchronous action and unwraps its <see cref="ActionResult{TValue}"/>.
    /// </summary>
    public static TValue? Invoke<TController, TValue>(
        this Controller mvc,
        Func<TController, ActionResult<TValue>> action)
        where TController : ControllerBase
    {
        var ctrl = ActivatorUtilities.CreateInstance<TController>(mvc.HttpContext.RequestServices);
        ctrl.ControllerContext = new ControllerContext { HttpContext = mvc.HttpContext };
        return UnwrapValue(action(ctrl));
    }

    /// <summary>
    /// Invokes an action returning <see cref="IActionResult"/> (no value type).
    /// Returns true if the result is a success status (2xx) or no explicit status.
    /// </summary>
    public static async Task<IActionResult> InvokeRawAsync<TController>(
        this Controller mvc,
        Func<TController, Task<IActionResult>> action)
        where TController : ControllerBase
    {
        var ctrl = ActivatorUtilities.CreateInstance<TController>(mvc.HttpContext.RequestServices);
        ctrl.ControllerContext = new ControllerContext { HttpContext = mvc.HttpContext };
        return await action(ctrl);
    }

    private static TValue? UnwrapValue<TValue>(ActionResult<TValue> result)
    {
        if (result.Value is not null) return result.Value;
        return result.Result switch
        {
            OkObjectResult { Value: TValue v } => v,
            ObjectResult { Value: TValue v2 } => v2,
            _ => default
        };
    }
}

using System.Security.Claims;

namespace Stocky.Api.Data;

public static class UserContextExtensions
{
    public static string GetOwnerId(this ClaimsPrincipal user)
    {
        // Entra ID object id claim
        return user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("Authenticated user has no object id claim.");
    }
}

using System.Security.Claims;

namespace Stocky.Api.Data;

public static class UserContextExtensions
{
    public static string GetOwnerId(this ClaimsPrincipal user)
    {
        // Google ID tokens use 'sub' as the stable user identifier.
        // Entra tokens use 'oid'; keep as fallback for any mixed deployments.
        return user.FindFirst("sub")?.Value
            ?? user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("Authenticated user has no stable identity claim.");
    }
}

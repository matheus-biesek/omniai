using System.Security.Claims;

namespace ApiGraphQL.Types;

public static class ClaimsPrincipalExtensions
{
    public static void RequireAuthenticated(this ClaimsPrincipal claimsPrincipal)
    {
        if (claimsPrincipal.Identity is not { IsAuthenticated: true })
        {
            throw new UnauthorizedAccessException("Autenticação necessária.");
        }
    }
}

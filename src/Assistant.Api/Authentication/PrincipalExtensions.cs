using System.Security.Claims;

namespace Assistant.Api.Authentication;

internal static class PrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

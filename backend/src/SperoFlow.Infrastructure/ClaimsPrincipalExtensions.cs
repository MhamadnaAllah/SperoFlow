using System.Security.Claims;

namespace SperoFlow.Infrastructure;

internal static class ClaimsPrincipalExtensions
{
    public static string? FindFirstValue(this ClaimsPrincipal principal, string type) =>
        principal.FindFirst(type)?.Value;
}

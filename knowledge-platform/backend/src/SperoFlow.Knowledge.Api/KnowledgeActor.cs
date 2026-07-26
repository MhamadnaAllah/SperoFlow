using System.Security.Claims;

namespace SperoFlow.Knowledge.Api;

public sealed record KnowledgeActor(string Subject, bool IsAdmin)
{
    public static KnowledgeActor FromPrincipal(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new UnauthorizedAccessException("The OIDC token does not contain an immutable subject claim.");
        }

        var roles = principal.FindAll("role").Select(claim => claim.Value)
            .Concat(principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value));
        var isAdmin = roles.Contains("KnowledgeAdmin", StringComparer.Ordinal) || roles.Contains("Admin", StringComparer.Ordinal);
        return new KnowledgeActor(subject, isAdmin);
    }
}
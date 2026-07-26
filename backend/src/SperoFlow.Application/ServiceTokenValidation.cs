using System.Security.Claims;

namespace SperoFlow.Application;

public interface IServiceTokenValidator
{
    ClaimsPrincipal? Validate(string token, string audience, string requiredScope);
}

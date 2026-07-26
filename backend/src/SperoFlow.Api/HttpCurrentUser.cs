using System.Security.Claims;
using SperoFlow.Application;

namespace SperoFlow.Api;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private readonly ClaimsPrincipal? _principal = httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => _principal?.Identity?.IsAuthenticated == true && UserId != Guid.Empty;

    public Guid UserId
    {
        get
        {
            var value = _principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
        }
    }

    public string? Email => _principal?.FindFirst(ClaimTypes.Email)?.Value;
}

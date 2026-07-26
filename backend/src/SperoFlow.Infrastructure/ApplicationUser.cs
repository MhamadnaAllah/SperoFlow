using Microsoft.AspNetCore.Identity;

namespace SperoFlow.Infrastructure;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }

    public bool IsActive { get; set; } = true;
}

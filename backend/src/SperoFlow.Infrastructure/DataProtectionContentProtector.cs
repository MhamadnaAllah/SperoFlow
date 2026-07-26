using Microsoft.AspNetCore.DataProtection;
using SperoFlow.Application;

namespace SperoFlow.Infrastructure;

public sealed class DataProtectionContentProtector(IDataProtectionProvider provider) : IContentProtector
{
    private const string Purpose = "SperoFlow.UserContent.v1";

    public string Protect(Guid ownerId, string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return CreateProtector(ownerId).Protect(plaintext);
    }

    public string Unprotect(Guid ownerId, string protectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        return CreateProtector(ownerId).Unprotect(protectedValue);
    }

    private IDataProtector CreateProtector(Guid ownerId) =>
        provider.CreateProtector(Purpose, ownerId.ToString("N", System.Globalization.CultureInfo.InvariantCulture));
}

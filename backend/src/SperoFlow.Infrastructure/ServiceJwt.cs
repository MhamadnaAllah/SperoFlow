using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SperoFlow.Application;

namespace SperoFlow.Infrastructure;

public sealed class RsaServiceTokenFactory : IServiceTokenFactory
{
    private readonly ServiceJwtOptions _options;
    private readonly SigningCredentials _credentials;

    public RsaServiceTokenFactory(IOptions<ServiceJwtOptions> options)
    {
        _options = options.Value;
        _credentials = new SigningCredentials(LoadKey(_options.PrivateKeyPath, _options.KeyId), SecurityAlgorithms.RsaSha256);
    }

    public string CreateToken(
        string audience,
        string scope,
        Guid? userId,
        TimeSpan lifetime,
        IReadOnlyDictionary<string, string>? additionalClaims = null)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "service:api"),
            new("scope", scope),
        };
        if (userId.HasValue)
        {
            claims.Add(new Claim("user_id", userId.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (additionalClaims is not null)
        {
            claims.AddRange(additionalClaims.Select(pair => new Claim(pair.Key, pair.Value)));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(lifetime),
            signingCredentials: _credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    internal static RsaSecurityKey LoadKey(string path, string keyId)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("The service JWT private key is not mounted.");
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(path));
        return new RsaSecurityKey(rsa) { KeyId = keyId };
    }
}

public sealed class RsaServiceTokenValidator : IServiceTokenValidator
{
    private readonly ServiceJwtOptions _options;
    private readonly SecurityKey _validationKey;

    public RsaServiceTokenValidator(IOptions<ServiceJwtOptions> options)
    {
        _options = options.Value;
        _validationKey = LoadValidationKey(_options);
    }

    public ClaimsPrincipal? Validate(string token, string audience, string requiredScope)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(
                token,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = _options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _validationKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                },
                out _);

            var scopes = principal.FindFirstValue("scope")?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? [];
            return scopes.Contains(requiredScope, StringComparer.Ordinal) ? principal : null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static SecurityKey LoadValidationKey(ServiceJwtOptions options)
    {
        var keyPath = string.IsNullOrWhiteSpace(options.PublicKeyPath) ? options.PrivateKeyPath : options.PublicKeyPath;
        if (!File.Exists(keyPath))
        {
            throw new InvalidOperationException("The service JWT validation key is not mounted.");
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(keyPath));
        return new RsaSecurityKey(rsa) { KeyId = options.KeyId };
    }
}

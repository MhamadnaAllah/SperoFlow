using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SperoFlow.Knowledge.Contracts;

namespace SperoFlow.Knowledge.Infrastructure;

public sealed class KnowledgeInternalTokenService
{
    private readonly KnowledgeInternalAuthOptions _options;
    private readonly Lazy<SecurityKey> _mainValidationKey;
    private readonly Lazy<SigningCredentials> _workerCredentials;
    private readonly Lazy<SecurityKey> _workerValidationKey;

    public KnowledgeInternalTokenService(IOptions<KnowledgeInternalAuthOptions> options)
    {
        _options = options.Value;
        _mainValidationKey = new Lazy<SecurityKey>(() => LoadKey(_options.MainPublicKeyPath, "speroflow-main-service"));
        _workerCredentials = new Lazy<SigningCredentials>(() => new SigningCredentials(
            LoadKey(_options.WorkerPrivateKeyPath, "speroflow-knowledge-worker"),
            SecurityAlgorithms.RsaSha256));
        _workerValidationKey = new Lazy<SecurityKey>(() => LoadKey(_options.WorkerPublicKeyPath, "speroflow-knowledge-worker"));
    }

    public ClaimsPrincipal? ValidateMainServiceToken(string token, string requiredScope) =>
        Validate(token, _options.MainIssuer, _options.MainAudience, _mainValidationKey.Value, requiredScope, null);

    public TimeSpan WorkerDeliveryTokenLifetime => TimeSpan.FromMinutes(_options.WorkerDeliveryTokenLifetimeMinutes);

    public TimeSpan WorkerLeaseDuration => TimeSpan.FromMinutes(_options.WorkerLeaseDurationMinutes);

    public ClaimsPrincipal? ValidateWorkerDeliveryToken(string token, Guid jobId) =>
        Validate(token, _options.WorkerIssuer, _options.WorkerAudience, _workerValidationKey.Value, "knowledge.jobs.claim", jobId);

    public ClaimsPrincipal? ValidateWorkerExecutionToken(string token, Guid jobId) =>
        Validate(token, _options.WorkerIssuer, _options.WorkerAudience, _workerValidationKey.Value, "knowledge.jobs.execute", jobId);

    public static bool MatchesWorkerAttempt(ClaimsPrincipal principal, int expectedAttempt) =>
        expectedAttempt > 0 &&
        int.TryParse(
            principal.FindFirst("attempt")?.Value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var actualAttempt) &&
        actualAttempt == expectedAttempt;

    public static bool MatchesWorkerLease(ClaimsPrincipal principal, Guid expectedLeaseId) =>
        expectedLeaseId != Guid.Empty &&
        Guid.TryParse(principal.FindFirst("lease_id")?.Value, out var actualLeaseId) &&
        actualLeaseId == expectedLeaseId;

    public string CreateWorkerDeliveryToken(Guid jobId, int attempt)
    {
        if (jobId == Guid.Empty || attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "A delivery token requires a job and positive dispatch attempt.");
        }

        return CreateWorkerToken(jobId, attempt, null, "knowledge.jobs.claim", DateTimeOffset.UtcNow.Add(WorkerDeliveryTokenLifetime));
    }

    public string CreateWorkerExecutionToken(Guid jobId, int attempt, Guid leaseId, DateTimeOffset expiresAt)
    {
        if (jobId == Guid.Empty || attempt < 1 || leaseId == Guid.Empty || expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseId), "An execution token requires a live job lease.");
        }

        return CreateWorkerToken(jobId, attempt, leaseId, "knowledge.jobs.execute", expiresAt);
    }

    private string CreateWorkerToken(Guid jobId, int attempt, Guid? leaseId, string scope, DateTimeOffset expiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "service:knowledge-worker"),
            new("scope", scope),
            new("job_id", jobId.ToString("D", System.Globalization.CultureInfo.InvariantCulture)),
            new("attempt", attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (leaseId.HasValue)
        {
            claims.Add(new Claim("lease_id", leaseId.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture)));
        }

        var token = new JwtSecurityToken(
            issuer: _options.WorkerIssuer,
            audience: _options.WorkerAudience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _workerCredentials.Value);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }    private static ClaimsPrincipal? Validate(string token, string issuer, string audience, SecurityKey key, string requiredScope, Guid? requiredJobId)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(
                token,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                },
                out _);
            var scopes = principal.FindFirst("scope")?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            if (!scopes.Contains(requiredScope, StringComparer.Ordinal))
            {
                return null;
            }

            if (requiredJobId.HasValue && !string.Equals(principal.FindFirst("job_id")?.Value, requiredJobId.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return principal;
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

    internal static RsaSecurityKey LoadKey(string path, string keyId)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The required RSA key is not mounted: {path}");
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(path));
        return new RsaSecurityKey(rsa) { KeyId = keyId };
    }
}

public sealed class KnowledgeAccessGrantService
{
    private readonly KnowledgeGrantOptions _options;
    private readonly SigningCredentials _credentials;

    public KnowledgeAccessGrantService(IOptions<KnowledgeGrantOptions> options)
    {
        _options = options.Value;
        _credentials = new SigningCredentials(KnowledgeInternalTokenService.LoadKey(_options.PrivateKeyPath, _options.KeyId), SecurityAlgorithms.RsaSha256);
    }

    public (string Token, DateTimeOffset ExpiresAt) Issue(string subject, IReadOnlyCollection<KnowledgeGrantDataset> datasets)
    {
        if (string.IsNullOrWhiteSpace(subject) || datasets.Count is < 1 or > 20 || datasets.Any(value => value.DatasetId == Guid.Empty || string.IsNullOrWhiteSpace(value.ReleaseKey)))
        {
            throw new InvalidOperationException("A bounded subject and between one and twenty datasets are required for a knowledge access grant.");
        }

        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("D", System.Globalization.CultureInfo.InvariantCulture)),
            new("scope", "knowledge.query"),
        };
        claims.AddRange(datasets.DistinctBy(value => value.DatasetId).OrderBy(value => value.DatasetId).Select(value => new Claim("dataset_grant", JsonSerializer.Serialize(new { dataset_id = value.DatasetId.ToString("D", System.Globalization.CultureInfo.InvariantCulture), release_key = value.ReleaseKey.Trim(), owner_subject = value.OwnerSubject, visibility = value.Visibility.ToString().ToLowerInvariant() }))));
        var expires = now.AddSeconds(_options.LifetimeSeconds);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
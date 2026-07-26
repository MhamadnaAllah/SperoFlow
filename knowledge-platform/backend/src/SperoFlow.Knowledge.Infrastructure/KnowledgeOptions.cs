using System.ComponentModel.DataAnnotations;

namespace SperoFlow.Knowledge.Infrastructure;

public sealed class KnowledgeDatabaseOptions
{
    public const string SectionName = "KnowledgeDatabase";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;
}

public sealed class KnowledgeStorageOptions : IValidatableObject
{
    public const string SectionName = "KnowledgeStorage";

    [Required]
    public string BucketName { get; init; } = "speroflow-knowledge";

    [Required]
    public string Region { get; init; } = "us-east-1";

    public string? Endpoint { get; init; }

    public string? PublicEndpoint { get; init; }

    public string AccessKey { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public bool UseSsl { get; init; }

    public bool PublicUseSsl { get; init; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(BucketName))
        {
            yield return new ValidationResult("KnowledgeStorage:BucketName is required.", [nameof(BucketName)]);
        }

        if (!string.IsNullOrWhiteSpace(Endpoint) && (string.IsNullOrWhiteSpace(AccessKey) || string.IsNullOrWhiteSpace(SecretKey)))
        {
            yield return new ValidationResult("S3-compatible storage requires an access key and secret key.", [nameof(AccessKey), nameof(SecretKey)]);
        }
    }
}

public sealed class KnowledgeRedisOptions
{
    public const string SectionName = "KnowledgeRedis";

    [Required]
    public string ConnectionString { get; init; } = string.Empty;

    [Required]
    public string JobsStream { get; init; } = "speroflow:knowledge:jobs";
}

public sealed class KnowledgeInternalAuthOptions
{
    public const string SectionName = "KnowledgeInternalAuth";

    [Required]
    public string MainIssuer { get; init; } = "speroflow-api";

    [Required]
    public string MainAudience { get; init; } = "speroflow-knowledge";

    [Required]
    public string MainPublicKeyPath { get; init; } = "/run/secrets/service_jwt_public_key";

    [Required]
    public string WorkerIssuer { get; init; } = "speroflow-knowledge-api";

    [Required]
    public string WorkerAudience { get; init; } = "speroflow-knowledge-api";

    [Required]
    public string WorkerPrivateKeyPath { get; init; } = "/run/secrets/knowledge_service_jwt_private_key";

    [Required]
    public string WorkerPublicKeyPath { get; init; } = "/run/secrets/knowledge_service_jwt_public_key";

    [Range(10, 120)]
    public int WorkerDeliveryTokenLifetimeMinutes { get; init; } = 30;

    [Range(5, 60)]
    public int WorkerLeaseDurationMinutes { get; init; } = 15;
}

public sealed class KnowledgeGrantOptions
{
    public const string SectionName = "KnowledgeGrants";

    [Required]
    public string Issuer { get; init; } = "speroflow-knowledge-api";

    [Required]
    public string Audience { get; init; } = "speroflow-ai";

    [Required]
    public string KeyId { get; init; } = "speroflow-knowledge-grant-1";

    [Required]
    public string PrivateKeyPath { get; init; } = "/run/secrets/knowledge_grant_private_key";

    [Required]
    public string PublicKeyPath { get; init; } = "/run/secrets/knowledge_grant_public_key";

    [Range(30, 300)]
    public int LifetimeSeconds { get; init; } = 90;
}

public sealed class KnowledgeOidcOptions : IValidatableObject
{
    public const string SectionName = "KnowledgeOidc";

    [Required]
    public string Authority { get; init; } = string.Empty;

    [Required]
    public string ClientId { get; init; } = "speroflow-knowledge-portal";

    public string CallbackPath { get; init; } = "/signin-oidc";

    public string SignedOutCallbackPath { get; init; } = "/signout-callback-oidc";

    public bool RequireHttpsMetadata { get; init; } = true;

    [Required]
    public string DataProtectionKeysDirectory { get; init; } = "/var/lib/speroflow/keys";

    [Required]
    public string DataProtectionCertificatePath { get; init; } = "/run/secrets/knowledge_portal_data_protection_certificate";

    [Required]
    public string DataProtectionCertificatePasswordPath { get; init; } = "/run/secrets/knowledge_portal_data_protection_certificate_password";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority) ||
            (RequireHttpsMetadata && !string.Equals(authority.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ValidationResult("KnowledgeOidc:Authority must be an absolute HTTPS URL when HTTPS metadata is required.", [nameof(Authority)]);
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            yield return new ValidationResult("KnowledgeOidc:ClientId is required.", [nameof(ClientId)]);
        }

        if (!CallbackPath.StartsWith('/') || CallbackPath.StartsWith("//", StringComparison.Ordinal))
        {
            yield return new ValidationResult("KnowledgeOidc:CallbackPath must be a local absolute path.", [nameof(CallbackPath)]);
        }

        if (!SignedOutCallbackPath.StartsWith('/') || SignedOutCallbackPath.StartsWith("//", StringComparison.Ordinal))
        {
            yield return new ValidationResult("KnowledgeOidc:SignedOutCallbackPath must be a local absolute path.", [nameof(SignedOutCallbackPath)]);
        }
    }
}

using System.ComponentModel.DataAnnotations;

namespace SperoFlow.Infrastructure;

public sealed class ObjectStorageOptions : IValidatableObject
{
    public const string SectionName = "ObjectStorage";

    /// <summary>
    /// Selects the object-store implementation. Use Minio locally and S3 (or AWS) in
    /// production. The S3 provider uses the AWS SDK default credential chain, including
    /// an ECS task role, instead of static access keys in configuration.
    /// </summary>
    [Required]
    public string Provider { get; init; } = "Minio";

    public string Endpoint { get; init; } = "object-storage:9000";

    public string AccessKey { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    [Required]
    public string BucketName { get; init; } = "speroflow-documents";

    public bool UseSsl { get; init; }

    public string? PublicEndpoint { get; init; }

    public bool PublicUseSsl { get; init; } = true;

    /// <summary>
    /// AWS region used by the canonical S3 provider, for example <c>eu-west-1</c>.
    /// </summary>
    public string Region { get; init; } = "us-east-1";

    /// <summary>
    /// Optional customer-managed KMS key ARN or ID. When omitted, S3 requests require
    /// SSE-S3 (<c>AES256</c>) instead.
    /// </summary>
    public string? KmsKeyId { get; init; }

    public bool UsesS3 => string.Equals(Provider, "S3", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Provider, "AWS", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UsesS3 && !string.Equals(Provider, "Minio", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                "ObjectStorage:Provider must be either 'S3' (or 'AWS') or 'Minio'.",
                [nameof(Provider)]);
        }

        if (string.IsNullOrWhiteSpace(BucketName))
        {
            yield return new ValidationResult("ObjectStorage:BucketName is required.", [nameof(BucketName)]);
        }

        if (UsesS3)
        {
            if (string.IsNullOrWhiteSpace(Region))
            {
                yield return new ValidationResult("ObjectStorage:Region is required when Provider is S3.", [nameof(Region)]);
            }

            yield break;
        }

        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            yield return new ValidationResult("ObjectStorage:Endpoint is required when Provider is Minio.", [nameof(Endpoint)]);
        }

        if (string.IsNullOrWhiteSpace(AccessKey) || string.IsNullOrWhiteSpace(SecretKey))
        {
            yield return new ValidationResult(
                "ObjectStorage:AccessKey and ObjectStorage:SecretKey are required when Provider is Minio.",
                [nameof(AccessKey), nameof(SecretKey)]);
        }
    }
}

public sealed class AiServiceOptions
{
    public const string SectionName = "AiService";

    [Required]
    public string BaseUrl { get; init; } = "http://ai-api:8000";
}


public sealed class KnowledgePlatformOptions
{
    public const string SectionName = "KnowledgePlatform";

    [Required]
    public string BaseUrl { get; init; } = "http://knowledge-api:8080";

    [Required]
    public string Audience { get; init; } = "speroflow-knowledge";
}
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required]
    public string ConnectionString { get; init; } = "redis:6379,abortConnect=false";

    [Required]
    public string AiJobsStream { get; init; } = "speroflow:ai:jobs";
}

public sealed class LegacyKnowledgeIngestionOptions
{
    public const string SectionName = "LegacyKnowledgeIngestion";

    // Retain legacy records for a verified migration only. New knowledge ingestion belongs to knowledge-platform.
    public bool Enabled { get; init; }
}
public sealed class ServiceJwtOptions
{
    public const string SectionName = "ServiceJwt";

    [Required]
    public string Issuer { get; init; } = "speroflow-api";

    [Required]
    public string KeyId { get; init; } = "speroflow-service-1";

    [Required]
    public string PrivateKeyPath { get; init; } = "/run/secrets/service_jwt_private_key";

    public string? PublicKeyPath { get; init; }

    [Required]
    public string AiAudience { get; init; } = "speroflow-ai";

    [Required]
    public string ApiAudience { get; init; } = "speroflow-api";
}

public sealed class AccountOptions
{
    public const string SectionName = "Accounts";

    [Required]
    public string PublicWebOrigin { get; init; } = "https://localhost";

    public bool RequireConfirmedEmail { get; init; } = true;

    public bool AllowPublicRegistration { get; init; }

    public string BootstrapRegistrationTokenPath { get; init; } = "/run/secrets/admin_bootstrap_token";
}

public sealed class IdentityServerOptions : IValidatableObject
{
    public const string SectionName = "IdentityServer";

    public bool Enabled { get; init; }

    public string Issuer { get; init; } = string.Empty;

    public string KnowledgePortalClientId { get; init; } = "speroflow-knowledge-portal";

    public string KnowledgePortalRedirectUri { get; init; } = string.Empty;
    public string SigningCertificatePath { get; init; } = "/run/secrets/oidc_signing_certificate";

    public string SigningCertificatePasswordPath { get; init; } = "/run/secrets/oidc_signing_certificate_password";

    public string EncryptionCertificatePath { get; init; } = "/run/secrets/oidc_encryption_certificate";

    public string EncryptionCertificatePasswordPath { get; init; } = "/run/secrets/oidc_encryption_certificate_password";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled)
        {
            yield break;
        }

        if (!Uri.TryCreate(Issuer, UriKind.Absolute, out var issuer) || !string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("IdentityServer:Issuer must be an absolute HTTPS URL.", [nameof(Issuer)]);
        }

        if (string.IsNullOrWhiteSpace(KnowledgePortalClientId))
        {
            yield return new ValidationResult("IdentityServer:KnowledgePortalClientId is required.", [nameof(KnowledgePortalClientId)]);
        }

        if (!Uri.TryCreate(KnowledgePortalRedirectUri, UriKind.Absolute, out var callback) || !string.Equals(callback.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("IdentityServer:KnowledgePortalRedirectUri must be an absolute HTTPS URL.", [nameof(KnowledgePortalRedirectUri)]);
        }


    }
}

public sealed class RoleDiscoveryOptions
{
    public const string SectionName = "RoleDiscovery";

    public bool Enabled { get; init; } = true;

    [Range(1, 168)]
    public int SweepIntervalHours { get; init; } = 24;
}
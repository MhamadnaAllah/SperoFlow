using System.Security.Cryptography;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace SperoFlow.Knowledge.Infrastructure;

public sealed record KnowledgePresignedUpload(
    string ObjectKey,
    string UploadUrl,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);

public sealed record KnowledgeStoredObject(string ObjectKey, long SizeBytes, string ContentType, string Sha256);

public interface IKnowledgeObjectStorage
{
    Task<KnowledgePresignedUpload> CreatePresignedUploadAsync(string objectKey, string contentType, TimeSpan lifetime, CancellationToken cancellationToken);

    Task<KnowledgeStoredObject> VerifyObjectAsync(string objectKey, string fileName, string expectedContentType, long expectedSizeBytes, string expectedSha256, CancellationToken cancellationToken);
}

public sealed class S3KnowledgeObjectStorage : IKnowledgeObjectStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly IAmazonS3 _presigningClient;
    private readonly KnowledgeStorageOptions _options;

    public S3KnowledgeObjectStorage(IAmazonS3 client, IOptions<KnowledgeStorageOptions> options)
    {
        _client = client;
        _options = options.Value;
        _presigningClient = string.IsNullOrWhiteSpace(_options.PublicEndpoint)
            ? client
            : CreateClient(_options, _options.PublicEndpoint, _options.PublicUseSsl);
    }

    public Task<KnowledgePresignedUpload> CreatePresignedUploadAsync(string objectKey, string contentType, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            ContentType = contentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
        };
        var uploadUrl = _presigningClient.GetPreSignedURL(request);

        return Task.FromResult(new KnowledgePresignedUpload(
            objectKey,
            uploadUrl,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = contentType,
                ["x-amz-server-side-encryption"] = "AES256",
            },
            expiresAt));
    }

    public async Task<KnowledgeStoredObject> VerifyObjectAsync(string objectKey, string fileName, string expectedContentType, long expectedSizeBytes, string expectedSha256, CancellationToken cancellationToken)
    {
        var metadata = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
        }, cancellationToken);
        if (metadata.ContentLength != expectedSizeBytes)
        {
            throw new InvalidOperationException("The uploaded object size does not match the approved size.");
        }

        var actualContentType = metadata.Headers.ContentType?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;
        if (!string.Equals(actualContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The uploaded object content type does not match the approved content type.");
        }

        using var response = await _client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
        }, cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        var signatureChecked = false;
        int count;
        while ((count = await response.ResponseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            if (!signatureChecked)
            {
                KnowledgeSourceSignatureValidator.Validate(fileName, actualContentType, buffer.AsSpan(0, Math.Min(count, 8_192)));
                signatureChecked = true;
            }

            hash.AppendData(buffer, 0, count);
        }

        if (!signatureChecked)
        {
            throw new InvalidOperationException("The uploaded source is empty.");
        }
        var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The uploaded object checksum does not match the approved checksum.");
        }

        return new KnowledgeStoredObject(objectKey, metadata.ContentLength, actualContentType, sha256);
    }

    public static AmazonS3Client CreateClient(KnowledgeStorageOptions options) =>
        CreateClient(options, options.Endpoint, options.UseSsl);

    private static AmazonS3Client CreateClient(KnowledgeStorageOptions options, string? endpoint, bool useSsl)
    {
        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
        };
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            config.ServiceURL = NormalizeEndpoint(endpoint, useSsl).ToString().TrimEnd('/');
            config.ForcePathStyle = true;
        }

        if (!string.IsNullOrWhiteSpace(options.AccessKey) && !string.IsNullOrWhiteSpace(options.SecretKey))
        {
            return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
        }

        return new AmazonS3Client(config);
    }

    public void Dispose()
    {
        if (!ReferenceEquals(_presigningClient, _client))
        {
            _presigningClient.Dispose();
        }
    }

    private static Uri NormalizeEndpoint(string endpoint, bool useSsl)
    {
        var value = endpoint.Contains("://", StringComparison.Ordinal) ? endpoint : (useSsl ? "https://" : "http://") + endpoint;
        return new Uri(value, UriKind.Absolute);
    }
}
using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SperoFlow.Application;

namespace SperoFlow.Infrastructure;

/// <summary>
/// Stores private application objects in Amazon S3. Credentials deliberately come from
/// the AWS SDK default chain so an ECS task role, rather than a long-lived access key,
/// is the production credential source.
/// </summary>
public sealed class S3ObjectStorage : IObjectStorage
{
    private readonly IAmazonS3 _client;
    private readonly ObjectStorageOptions _options;

    /// <summary>
    /// Initializes the S3-backed object store.
    /// </summary>
    public S3ObjectStorage(IAmazonS3 client, IOptions<ObjectStorageOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<StoredObject> PutTextAsync(
        Guid ownerId,
        string objectName,
        string content,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var bytes = Encoding.UTF8.GetBytes(content);
        var objectKey = ownerId.ToString("N", System.Globalization.CultureInfo.InvariantCulture)
            + "/"
            + Guid.CreateVersion7().ToString("N", System.Globalization.CultureInfo.InvariantCulture)
            + "-"
            + objectName;
        await using var stream = new MemoryStream(bytes, writable: false);
        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = contentType,
            AutoCloseStream = false,
        };
        ApplyServerSideEncryption(request);
        await _client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
        return new StoredObject(objectKey, bytes.LongLength, contentType);
    }

    /// <inheritdoc />
    public async Task<string> GetTextAsync(string objectKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        try
        {
            using var response = await _client.GetObjectAsync(
                new GetObjectRequest { BucketName = _options.BucketName, Key = objectKey },
                cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(
                response.ResponseStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 81920,
                leaveOpen: false);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("The requested object was not found.", exception);
        }
    }

    /// <inheritdoc />
    public async Task<PresignedUpload> CreatePresignedUploadAsync(
        string objectKey,
        string contentType,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(15))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Presigned upload URLs must expire within 15 minutes.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Protocol = Protocol.HTTPS,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(lifetime),
        };
        ApplyServerSideEncryption(request);
        var uploadUrl = await _client.GetPreSignedURLAsync(request).WaitAsync(cancellationToken).ConfigureAwait(false);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = contentType,
            ["x-amz-server-side-encryption"] = EncryptionHeaderValue,
        };
        if (!string.IsNullOrWhiteSpace(_options.KmsKeyId))
        {
            headers["x-amz-server-side-encryption-aws-kms-key-id"] = _options.KmsKeyId;
        }

        return new PresignedUpload(objectKey, uploadUrl, headers, DateTimeOffset.UtcNow.Add(lifetime));
    }

    /// <inheritdoc />
    public async Task<StoredObjectVerification> VerifyObjectAsync(
        string objectKey,
        string expectedContentType,
        long expectedSizeBytes,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        UploadedObjectValidation.ValidateExpected(expectedContentType, expectedSizeBytes, expectedSha256);

        try
        {
            using var response = await _client.GetObjectAsync(
                new GetObjectRequest { BucketName = _options.BucketName, Key = objectKey },
                cancellationToken).ConfigureAwait(false);
            UploadedObjectValidation.ValidateMetadata(
                response.Headers.ContentLength,
                response.Headers.ContentType,
                expectedContentType,
                expectedSizeBytes);
            var actualSha256 = UploadedObjectValidation.ComputeSha256AndValidateSignature(response.ResponseStream, expectedContentType);
            UploadedObjectValidation.EnsureHashMatches(actualSha256, expectedSha256);
            return new StoredObjectVerification(
                objectKey,
                response.Headers.ContentLength,
                UploadedObjectValidation.NormalizeContentType(expectedContentType),
                actualSha256);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("The uploaded object was not found.", exception);
        }
    }

    private string EncryptionHeaderValue => string.IsNullOrWhiteSpace(_options.KmsKeyId) ? "AES256" : "aws:kms";

    private void ApplyServerSideEncryption(PutObjectRequest request)
    {
        request.ServerSideEncryptionMethod = string.IsNullOrWhiteSpace(_options.KmsKeyId)
            ? ServerSideEncryptionMethod.AES256
            : ServerSideEncryptionMethod.AWSKMS;
        request.ServerSideEncryptionKeyManagementServiceKeyId = _options.KmsKeyId;
    }

    private void ApplyServerSideEncryption(GetPreSignedUrlRequest request)
    {
        request.ServerSideEncryptionMethod = string.IsNullOrWhiteSpace(_options.KmsKeyId)
            ? ServerSideEncryptionMethod.AES256
            : ServerSideEncryptionMethod.AWSKMS;
        request.ServerSideEncryptionKeyManagementServiceKeyId = _options.KmsKeyId;
    }
}

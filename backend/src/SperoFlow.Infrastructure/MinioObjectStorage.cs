using System.Text;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using SperoFlow.Application;

namespace SperoFlow.Infrastructure;

public sealed class MinioObjectStorage : IObjectStorage, IDisposable
{
    private readonly IMinioClient _client;
    private readonly IMinioClient _presignClient;
    private readonly ObjectStorageOptions _options;
    private readonly SemaphoreSlim _bucketGate = new(1, 1);

    public MinioObjectStorage(IMinioClient client, IOptions<ObjectStorageOptions> options)
    {
        _client = client;
        _options = options.Value;
        _presignClient = CreatePresignClient(_options, client);
    }

    public async Task<StoredObject> PutTextAsync(
        Guid ownerId,
        string objectName,
        string content,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        await EnsureBucketAsync(cancellationToken);

        var bytes = Encoding.UTF8.GetBytes(content);
        var objectKey = ownerId.ToString("N", System.Globalization.CultureInfo.InvariantCulture) + "/" + Guid.CreateVersion7().ToString("N", System.Globalization.CultureInfo.InvariantCulture) + "-" + objectName;
        await using var stream = new MemoryStream(bytes, writable: false);
        await _client.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(bytes.LongLength)
                .WithContentType(contentType),
            cancellationToken);

        return new StoredObject(objectKey, bytes.LongLength, contentType);
    }

    public async Task<string> GetTextAsync(string objectKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        await using var destination = new MemoryStream();
        await _client.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectKey)
                .WithCallbackStream(source => source.CopyTo(destination)),
            cancellationToken);
        return Encoding.UTF8.GetString(destination.ToArray());
    }

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

        await EnsureBucketAsync(cancellationToken);
        var expiresInSeconds = (int)Math.Ceiling(lifetime.TotalSeconds);
        var uploadUrl = await _presignClient.PresignedPutObjectAsync(
            new PresignedPutObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectKey)
                .WithExpiry(expiresInSeconds));
        return new PresignedUpload(
            objectKey,
            uploadUrl,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = contentType,
                ["x-amz-server-side-encryption"] = "AES256",
            },
            DateTimeOffset.UtcNow.Add(lifetime));
    }

    public async Task<StoredObjectVerification> VerifyObjectAsync(
        string objectKey,
        string expectedContentType,
        long expectedSizeBytes,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        UploadedObjectValidation.ValidateExpected(expectedContentType, expectedSizeBytes, expectedSha256);

        var stat = await _client.StatObjectAsync(
            new StatObjectArgs().WithBucket(_options.BucketName).WithObject(objectKey),
            cancellationToken);
        UploadedObjectValidation.ValidateMetadata(stat.Size, stat.ContentType, expectedContentType, expectedSizeBytes);

        string? actualHash = null;
        await _client.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(_options.BucketName)
                .WithObject(objectKey)
                .WithCallbackStream(source => actualHash = UploadedObjectValidation.ComputeSha256AndValidateSignature(source, expectedContentType)),
            cancellationToken);
        if (actualHash is null)
        {
            throw new InvalidOperationException("The uploaded object could not be read for verification.");
        }

        UploadedObjectValidation.EnsureHashMatches(actualHash, expectedSha256);
        return new StoredObjectVerification(
            objectKey,
            stat.Size,
            UploadedObjectValidation.NormalizeContentType(expectedContentType),
            actualHash);
    }

    public void Dispose()
    {
        _bucketGate.Dispose();
        if (!ReferenceEquals(_client, _presignClient) && _presignClient is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static IMinioClient CreatePresignClient(ObjectStorageOptions options, IMinioClient fallback)
    {
        if (string.IsNullOrWhiteSpace(options.PublicEndpoint) || string.Equals(options.PublicEndpoint, options.Endpoint, StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        var builder = new MinioClient()
            .WithEndpoint(options.PublicEndpoint)
            .WithCredentials(options.AccessKey, options.SecretKey);
        if (options.PublicUseSsl)
        {
            builder = builder.WithSSL();
        }

        return builder.Build();
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        if (await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_options.BucketName), cancellationToken))
        {
            return;
        }

        await _bucketGate.WaitAsync(cancellationToken);
        try
        {
            if (!await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_options.BucketName), cancellationToken))
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_options.BucketName), cancellationToken);
            }
        }
        finally
        {
            _bucketGate.Release();
        }
    }
}




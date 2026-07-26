using SperoFlow.Contracts;

namespace SperoFlow.Application;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    string? Email { get; }
}

public interface IContentProtector
{
    string Protect(Guid ownerId, string plaintext);

    string Unprotect(Guid ownerId, string protectedValue);
}

public interface IObjectStorage
{
    Task<StoredObject> PutTextAsync(
        Guid ownerId,
        string objectName,
        string content,
        string contentType,
        CancellationToken cancellationToken);

    Task<string> GetTextAsync(string objectKey, CancellationToken cancellationToken);

    Task<PresignedUpload> CreatePresignedUploadAsync(
        string objectKey,
        string contentType,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    Task<StoredObjectVerification> VerifyObjectAsync(
        string objectKey,
        string expectedContentType,
        long expectedSizeBytes,
        string expectedSha256,
        CancellationToken cancellationToken);
}

public sealed record StoredObject(string ObjectKey, long SizeBytes, string ContentType);

public sealed record PresignedUpload(
    string ObjectKey,
    string UploadUrl,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);

public sealed record StoredObjectVerification(string ObjectKey, long SizeBytes, string ContentType, string Sha256);

public interface IServiceTokenFactory
{
    string CreateToken(
        string audience,
        string scope,
        Guid? userId,
        TimeSpan lifetime,
        IReadOnlyDictionary<string, string>? additionalClaims = null);
}

public sealed record KnowledgeCatalogItem(Guid Id, string Name, string Visibility, DateTimeOffset UpdatedAt);

public sealed record KnowledgeAccessGrant(string AccessGrant, DateTimeOffset ExpiresAt, IReadOnlyCollection<Guid> DatasetIds);

public interface IKnowledgePlatformGateway
{
    Task<IReadOnlyList<KnowledgeCatalogItem>> ListCatalogAsync(Guid userId, CancellationToken cancellationToken);

    Task<KnowledgeAccessGrant> IssueAccessGrantAsync(Guid userId, IReadOnlyCollection<Guid> datasetIds, CancellationToken cancellationToken);
}

public interface IAiGateway
{
    Task<GraphQueryResponse> QueryGraphAsync(GraphQueryRequest request, Guid userId, string? knowledgeAccessGrant, CancellationToken cancellationToken);

    Task<JsonDocument> InvokeAsync(
        string path,
        object payload,
        Guid userId,
        string scope,
        CancellationToken cancellationToken);
}

public interface IOutboxDispatcher
{
    Task<int> DispatchPendingAsync(CancellationToken cancellationToken);
}

public sealed record RoleDiscoveryRunResult(int EvidenceCount, IReadOnlyList<Guid> ProposalIds);

public interface IRoleDiscoveryService
{
    Task<RoleDiscoveryRunResult> DiscoverAsync(Guid ownerId, CancellationToken cancellationToken);
}
using SperoFlow.Knowledge.Domain;

namespace SperoFlow.Knowledge.Contracts;

public sealed record KnowledgeDatasetResponse(
    Guid Id,
    string OwnerSubject,
    string Name,
    string? Description,
    KnowledgeDatasetState State,
    KnowledgeVisibility Visibility,
    Guid? PublishedReleaseId,
    int SourceFileCount,
    int CompletedSourceFileCount,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record KnowledgeSourceResponse(
    Guid Id,
    Guid DatasetId,
    string FileName,
    string ContentType,
    long ExpectedSizeBytes,
    string ExpectedSha256,
    KnowledgeSourceState State,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record KnowledgeJobResponse(
    Guid Id,
    Guid DatasetId,
    Guid SourceFileId,
    Guid ReleaseId,
    KnowledgeIngestionState State,
    int AttemptCount,
    string? TextractJobId,
    string Report,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record KnowledgeReleaseResponse(
    Guid Id,
    Guid DatasetId,
    string ReleaseKey,
    KnowledgeReleaseState State,
    string? ValidationReport,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateKnowledgeDatasetRequest(string Name, string? Description = null);

public sealed record UpdateKnowledgeDatasetRequest(string Name, string? Description, Guid ConcurrencyToken);

public sealed record AssignKnowledgeOwnerRequest(string OwnerSubject, Guid ConcurrencyToken);

public sealed record ConcurrencyTokenRequest(Guid ConcurrencyToken);

public sealed record PublishKnowledgeDatasetRequest(Guid ReleaseId, Guid ConcurrencyToken);

public sealed record IssueKnowledgeUploadRequest(string FileName, string ContentType, long SizeBytes, string Sha256);

public sealed record PresignedKnowledgeUploadResponse(
    KnowledgeSourceResponse Source,
    string UploadUrl,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);

public sealed record FinalizeKnowledgeUploadResponse(KnowledgeSourceResponse Source, KnowledgeJobResponse Job);

public sealed record InternalKnowledgeJobResponse(
    Guid JobId,
    Guid DatasetId,
    Guid SourceId,
    Guid ReleaseId,
    string ReleaseKey,
    string OwnerSubject,
    string DatasetName,
    string ObjectKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    KnowledgeIngestionState State,
    string? TextractJobId,
    string ExecutionToken,
    DateTimeOffset LeaseExpiresAt);

public sealed record KnowledgeWorkerLeaseResponse(string ExecutionToken, DateTimeOffset LeaseExpiresAt);

public sealed record CompleteKnowledgeJobRequest(
    KnowledgeIngestionState State,
    string Report,
    int ContentUnits,
    int Entities,
    int Facts,
    int Vectors,
    string? Error = null,
    string? TextractJobId = null);

public sealed record KnowledgeAccessGrantRequest(string Subject, IReadOnlyCollection<Guid> DatasetIds);

public sealed record KnowledgeGrantDataset(Guid DatasetId, string ReleaseKey, string OwnerSubject, KnowledgeVisibility Visibility);

public sealed record KnowledgeAccessGrantResponse(string AccessGrant, DateTimeOffset ExpiresAt, IReadOnlyCollection<Guid> DatasetIds);

public sealed record KnowledgeCatalogItem(Guid Id, string Name, KnowledgeVisibility Visibility, DateTimeOffset UpdatedAt);

public sealed record KnowledgeOutboxEvent(Guid JobId, Guid DatasetId, Guid SourceId, Guid ReleaseId);
namespace SperoFlow.Knowledge.Domain;

public enum KnowledgeDatasetState
{
    Active,
    Archived,
}

public enum KnowledgeVisibility
{
    Private,
    PendingReview,
    Published,
}

public enum KnowledgeSourceState
{
    PendingUpload,
    Uploaded,
    Queued,
    Processing,
    Completed,
    Failed,
}

public enum KnowledgeIngestionState
{
    Queued,
    Processing,
    WaitingForOcr,
    Succeeded,
    SucceededWithWarnings,
    Failed,
}

public enum KnowledgeReleaseState
{
    Draft,
    Validated,
    Published,
    Superseded,
    Failed,
}

public sealed class KnowledgeValidationException : InvalidOperationException
{
    public KnowledgeValidationException(string message) : base(message)
    {
    }
}

public abstract class KnowledgeEntity
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public Guid ConcurrencyToken { get; private set; } = Guid.CreateVersion7();

    protected void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.CreateVersion7();
    }
}

public sealed class KnowledgeDataset : KnowledgeEntity
{
    private KnowledgeDataset()
    {
    }

    public KnowledgeDataset(string ownerSubject, string name, string? description)
    {
        OwnerSubject = RequireSubject(ownerSubject);
        Name = RequireText(name, "A dataset name is required.", 240);
        Description = NormalizeOptional(description, 8_000);
    }

    public string OwnerSubject { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public KnowledgeDatasetState State { get; private set; } = KnowledgeDatasetState.Active;

    public KnowledgeVisibility Visibility { get; private set; } = KnowledgeVisibility.Private;

    public Guid? PublishedReleaseId { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public void Update(string name, string? description)
    {
        EnsureActive();
        Name = RequireText(name, "A dataset name is required.", 240);
        Description = NormalizeOptional(description, 8_000);
        Touch();
    }

    public void AssignOwner(string ownerSubject)
    {
        EnsureActive();
        OwnerSubject = RequireSubject(ownerSubject);
        Visibility = KnowledgeVisibility.Private;
        PublishedReleaseId = null;
        PublishedAt = null;
        Touch();
    }

    public void SubmitForReview()
    {
        EnsureActive();
        if (Visibility != KnowledgeVisibility.Private)
        {
            throw new KnowledgeValidationException("Only private datasets can be submitted for review.");
        }

        Visibility = KnowledgeVisibility.PendingReview;
        Touch();
    }

    public void ReturnToPrivate()
    {
        EnsureActive();
        Visibility = KnowledgeVisibility.Private;
        PublishedReleaseId = null;
        PublishedAt = null;
        Touch();
    }

    public void Publish(Guid releaseId)
    {
        EnsureActive();
        if (Visibility != KnowledgeVisibility.PendingReview)
        {
            throw new KnowledgeValidationException("Only datasets pending review can be published.");
        }

        if (releaseId == Guid.Empty)
        {
            throw new KnowledgeValidationException("A validated graph release is required for publication.");
        }

        Visibility = KnowledgeVisibility.Published;
        PublishedReleaseId = releaseId;
        PublishedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Archive()
    {
        State = KnowledgeDatasetState.Archived;
        PublishedReleaseId = null;
        PublishedAt = null;
        Touch();
    }

    public void Restore()
    {
        State = KnowledgeDatasetState.Active;
        Touch();
    }

    private void EnsureActive()
    {
        if (State != KnowledgeDatasetState.Active)
        {
            throw new KnowledgeValidationException("This dataset is archived.");
        }
    }

    internal static string RequireSubject(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 256)
        {
            throw new KnowledgeValidationException("A bounded immutable owner subject is required.");
        }

        return normalized;
    }

    internal static string RequireText(string value, string message, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length < 1 || normalized.Length > maxLength)
        {
            throw new KnowledgeValidationException(message);
        }

        return normalized;
    }

    internal static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new KnowledgeValidationException("The supplied value is too long.");
        }

        return normalized;
    }
}

public sealed class KnowledgeSourceFile : KnowledgeEntity
{
    private KnowledgeSourceFile()
    {
    }

    public KnowledgeSourceFile(
        Guid datasetId,
        string ownerSubject,
        string fileName,
        string objectKey,
        string contentType,
        long expectedSizeBytes,
        string expectedSha256)
    {
        if (datasetId == Guid.Empty)
        {
            throw new KnowledgeValidationException("A dataset is required for every source file.");
        }

        DatasetId = datasetId;
        OwnerSubject = KnowledgeDataset.RequireSubject(ownerSubject);
        FileName = KnowledgeDataset.RequireText(fileName, "A source file name is required.", 500);
        ObjectKey = KnowledgeDataset.RequireText(objectKey, "An object key is required.", 1_024);
        ContentType = KnowledgeDataset.RequireText(contentType, "A source content type is required.", 200).ToLowerInvariant();
        if (expectedSizeBytes < 1 || expectedSizeBytes > 100L * 1024 * 1024)
        {
            throw new KnowledgeValidationException("Knowledge sources must be between 1 byte and 100 MB.");
        }

        if (expectedSha256?.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
        {
            throw new KnowledgeValidationException("A SHA-256 checksum is required for every source file.");
        }

        ExpectedSizeBytes = expectedSizeBytes;
        ExpectedSha256 = expectedSha256.ToLowerInvariant();
    }

    public Guid DatasetId { get; private set; }

    public string OwnerSubject { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public string ObjectKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long ExpectedSizeBytes { get; private set; }

    public string ExpectedSha256 { get; private set; } = string.Empty;

    public long? UploadedSizeBytes { get; private set; }

    public string? UploadedSha256 { get; private set; }

    public KnowledgeSourceState State { get; private set; } = KnowledgeSourceState.PendingUpload;

    public string? FailureReason { get; private set; }

    public void ConfirmUpload(long sizeBytes, string sha256, string contentType)
    {
        if (State != KnowledgeSourceState.PendingUpload)
        {
            throw new KnowledgeValidationException("This source upload has already been finalized.");
        }

        if (sizeBytes != ExpectedSizeBytes || !string.Equals(sha256, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnowledgeValidationException("The uploaded source does not match the approved checksum or size.");
        }

        if (!string.Equals(contentType, ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new KnowledgeValidationException("The uploaded source content type does not match the approved content type.");
        }

        UploadedSizeBytes = sizeBytes;
        UploadedSha256 = sha256.ToLowerInvariant();
        State = KnowledgeSourceState.Uploaded;
        FailureReason = null;
        Touch();
    }

    public void Queue()
    {
        if (State is not (KnowledgeSourceState.Uploaded or KnowledgeSourceState.Failed))
        {
            throw new KnowledgeValidationException("Only uploaded or failed sources can be queued.");
        }

        State = KnowledgeSourceState.Queued;
        FailureReason = null;
        Touch();
    }

    public void RequeueForRetry()
    {
        if (State is not (KnowledgeSourceState.Failed or KnowledgeSourceState.Processing))
        {
            throw new KnowledgeValidationException("Only failed or processing sources can be retried.");
        }

        State = KnowledgeSourceState.Queued;
        FailureReason = null;
        Touch();
    }

    public void MarkProcessing()
    {
        if (State == KnowledgeSourceState.Processing)
        {
            return;
        }

        if (State is not (KnowledgeSourceState.Queued or KnowledgeSourceState.Failed))
        {
            throw new KnowledgeValidationException("Only queued sources can begin processing.");
        }

        State = KnowledgeSourceState.Processing;
        FailureReason = null;
        Touch();
    }

    public void MarkCompleted()
    {
        State = KnowledgeSourceState.Completed;
        FailureReason = null;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        State = KnowledgeSourceState.Failed;
        FailureReason = KnowledgeDataset.RequireText(reason, "A failure reason is required.", 1_000);
        Touch();
    }

    public void AssignOwner(string ownerSubject)
    {
        OwnerSubject = KnowledgeDataset.RequireSubject(ownerSubject);
        Touch();
    }
}

public sealed class KnowledgeGraphRelease : KnowledgeEntity
{
    private KnowledgeGraphRelease()
    {
    }

    public KnowledgeGraphRelease(Guid datasetId, string ownerSubject, string releaseKey)
    {
        if (datasetId == Guid.Empty)
        {
            throw new KnowledgeValidationException("A graph release requires a dataset.");
        }

        DatasetId = datasetId;
        OwnerSubject = KnowledgeDataset.RequireSubject(ownerSubject);
        ReleaseKey = KnowledgeDataset.RequireText(releaseKey, "A release key is required.", 200);
    }

    public Guid DatasetId { get; private set; }

    public string OwnerSubject { get; private set; } = string.Empty;

    public string ReleaseKey { get; private set; } = string.Empty;

    public KnowledgeReleaseState State { get; private set; } = KnowledgeReleaseState.Draft;

    public string? ValidationReport { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public void MarkValidated(string report)
    {
        if (State != KnowledgeReleaseState.Draft)
        {
            throw new KnowledgeValidationException("Only a draft graph release can be validated.");
        }

        State = KnowledgeReleaseState.Validated;
        ValidationReport = KnowledgeDataset.RequireText(report, "A validation report is required.", 32_000);
        Touch();
    }

    public void ReopenForRetry()
    {
        if (State != KnowledgeReleaseState.Failed)
        {
            throw new KnowledgeValidationException("Only a failed graph release can be reopened for retry.");
        }

        State = KnowledgeReleaseState.Draft;
        ValidationReport = null;
        Touch();
    }

    public void Publish()
    {
        if (State != KnowledgeReleaseState.Validated)
        {
            throw new KnowledgeValidationException("Only validated graph releases can be published.");
        }

        State = KnowledgeReleaseState.Published;
        PublishedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Supersede()
    {
        State = KnowledgeReleaseState.Superseded;
        Touch();
    }

    public void MarkFailed(string report)
    {
        State = KnowledgeReleaseState.Failed;
        ValidationReport = KnowledgeDataset.RequireText(report, "A failure report is required.", 32_000);
        Touch();
    }
}

public sealed class KnowledgeIngestionJob : KnowledgeEntity
{
    private KnowledgeIngestionJob()
    {
    }

    public KnowledgeIngestionJob(Guid datasetId, Guid sourceFileId, Guid releaseId, string ownerSubject)
    {
        if (datasetId == Guid.Empty || sourceFileId == Guid.Empty || releaseId == Guid.Empty)
        {
            throw new KnowledgeValidationException("An ingestion job requires a dataset, source, and graph release.");
        }

        DatasetId = datasetId;
        SourceFileId = sourceFileId;
        ReleaseId = releaseId;
        OwnerSubject = KnowledgeDataset.RequireSubject(ownerSubject);
    }

    public Guid DatasetId { get; private set; }

    public Guid SourceFileId { get; private set; }

    public Guid ReleaseId { get; private set; }

    public string OwnerSubject { get; private set; } = string.Empty;

    public KnowledgeIngestionState State { get; private set; } = KnowledgeIngestionState.Queued;

    public int AttemptCount { get; private set; }

    public Guid? LeaseId { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public DateTimeOffset? DispatchExpiresAt { get; private set; }

    public string? TextractJobId { get; private set; }

    public string Report { get; private set; } = "{}";

    public string? FailureReason { get; private set; }

    public void MarkProcessing(DateTimeOffset? dispatchExpiresAt = null)
    {
        if (State == KnowledgeIngestionState.Processing)
        {
            return;
        }

        if (State is not (KnowledgeIngestionState.Queued or KnowledgeIngestionState.Failed or KnowledgeIngestionState.WaitingForOcr))
        {
            throw new KnowledgeValidationException("Only queued, failed, or OCR-waiting ingestion jobs can begin processing.");
        }

        var deadline = (dispatchExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(30)).ToUniversalTime();
        if (deadline <= DateTimeOffset.UtcNow)
        {
            throw new KnowledgeValidationException("A future delivery deadline is required for an ingestion job.");
        }

        State = KnowledgeIngestionState.Processing;
        AttemptCount++;
        DispatchExpiresAt = deadline;
        LeaseId = null;
        LeaseExpiresAt = null;
        FailureReason = null;
        Touch();
    }

    public bool TryAcquireLease(Guid leaseId, DateTimeOffset now, TimeSpan duration)
    {
        if (leaseId == Guid.Empty || duration <= TimeSpan.Zero || State != KnowledgeIngestionState.Processing)
        {
            return false;
        }

        now = now.ToUniversalTime();
        if (LeaseExpiresAt is { } activeUntil && activeUntil > now)
        {
            return LeaseId == leaseId;
        }

        LeaseId = leaseId;
        LeaseExpiresAt = now.Add(duration);
        Touch();
        return true;
    }

    public bool TryRenewLease(Guid leaseId, DateTimeOffset now, TimeSpan duration)
    {
        if (leaseId == Guid.Empty || duration <= TimeSpan.Zero || State != KnowledgeIngestionState.Processing || LeaseId != leaseId)
        {
            return false;
        }

        now = now.ToUniversalTime();
        if (LeaseExpiresAt is not { } activeUntil || activeUntil <= now)
        {
            return false;
        }

        LeaseExpiresAt = now.Add(duration);
        Touch();
        return true;
    }

    public bool HasActiveLease(Guid leaseId, DateTimeOffset now) =>
        State == KnowledgeIngestionState.Processing &&
        leaseId != Guid.Empty &&
        LeaseId == leaseId &&
        LeaseExpiresAt is { } activeUntil && activeUntil > now.ToUniversalTime();

    public bool IsLeaseExpired(DateTimeOffset now) =>
        State == KnowledgeIngestionState.Processing &&
        (LeaseExpiresAt ?? DispatchExpiresAt) is { } expiry && expiry <= now.ToUniversalTime();

    public bool RequeueExpiredLease(DateTimeOffset now)
    {
        if (!IsLeaseExpired(now))
        {
            return false;
        }

        State = KnowledgeIngestionState.Queued;
        LeaseId = null;
        LeaseExpiresAt = null;
        DispatchExpiresAt = null;
        FailureReason = null;
        Touch();
        return true;
    }

    public void MarkWaitingForOcr(string textractJobId, string report)
    {
        State = KnowledgeIngestionState.WaitingForOcr;
        LeaseId = null;
        LeaseExpiresAt = null;
        DispatchExpiresAt = null;
        TextractJobId = KnowledgeDataset.RequireText(textractJobId, "A Textract job ID is required.", 256);
        Report = KnowledgeDataset.RequireText(report, "An OCR status report is required.", 32_000);
        Touch();
    }

    public void MarkSucceeded(string report, bool warnings)
    {
        State = warnings ? KnowledgeIngestionState.SucceededWithWarnings : KnowledgeIngestionState.Succeeded;
        LeaseId = null;
        LeaseExpiresAt = null;
        DispatchExpiresAt = null;
        Report = KnowledgeDataset.RequireText(report, "A completion report is required.", 32_000);
        FailureReason = null;
        Touch();
    }

    public void MarkFailed(string reason, string report)
    {
        State = KnowledgeIngestionState.Failed;
        LeaseId = null;
        LeaseExpiresAt = null;
        DispatchExpiresAt = null;
        FailureReason = KnowledgeDataset.RequireText(reason, "A failure reason is required.", 1_000);
        Report = KnowledgeDataset.RequireText(report, "A failure report is required.", 32_000);
        Touch();
    }

    public void Retry()
    {
        if (State is not (KnowledgeIngestionState.Failed or KnowledgeIngestionState.WaitingForOcr))
        {
            throw new KnowledgeValidationException("Only failed or OCR-waiting jobs can be retried.");
        }

        var preserveTextractJob = State == KnowledgeIngestionState.WaitingForOcr;
        State = KnowledgeIngestionState.Queued;
        TextractJobId = preserveTextractJob ? TextractJobId : null;
        LeaseId = null;
        LeaseExpiresAt = null;
        DispatchExpiresAt = null;
        FailureReason = null;
        Touch();
    }
}

public sealed class KnowledgeOutboxMessage : KnowledgeEntity
{
    private KnowledgeOutboxMessage()
    {
    }

    public KnowledgeOutboxMessage(string ownerSubject, string type, string payload, DateTimeOffset? availableAt = null)
    {
        OwnerSubject = KnowledgeDataset.RequireSubject(ownerSubject);
        Type = KnowledgeDataset.RequireText(type, "An outbox message type is required.", 200);
        Payload = KnowledgeDataset.RequireText(payload, "An outbox payload is required.", 64_000);
        AvailableAt = availableAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
    }

    public string OwnerSubject { get; private set; } = string.Empty;

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset AvailableAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public void MarkAttempted()
    {
        Attempts++;
        Touch();
    }

    public void MarkDispatched()
    {
        DispatchedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}

public sealed class KnowledgeAuditEvent : KnowledgeEntity
{
    private KnowledgeAuditEvent()
    {
    }

    public KnowledgeAuditEvent(string actorSubject, string action, string entityType, Guid entityId, string? detail = null)
    {
        ActorSubject = KnowledgeDataset.RequireSubject(actorSubject);
        Action = KnowledgeDataset.RequireText(action, "An audit action is required.", 160);
        EntityType = KnowledgeDataset.RequireText(entityType, "An audit entity type is required.", 160);
        if (entityId == Guid.Empty)
        {
            throw new KnowledgeValidationException("An audit entity ID is required.");
        }

        EntityId = entityId;
        Detail = KnowledgeDataset.NormalizeOptional(detail, 4_000);
    }

    public string ActorSubject { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }

    public string? Detail { get; private set; }
}
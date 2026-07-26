using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Application;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static readonly Dictionary<string, IReadOnlySet<string>> DatasetUploadContentTypes =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [".csv"] = new HashSet<string>(["text/csv", "application/csv", "application/vnd.ms-excel"], StringComparer.OrdinalIgnoreCase),
            [".json"] = new HashSet<string>(["application/json", "text/json"], StringComparer.OrdinalIgnoreCase),
            [".md"] = new HashSet<string>(["text/markdown", "text/plain"], StringComparer.OrdinalIgnoreCase),
            [".txt"] = new HashSet<string>(["text/plain"], StringComparer.OrdinalIgnoreCase),
            [".docx"] = new HashSet<string>(["application/vnd.openxmlformats-officedocument.wordprocessingml.document"], StringComparer.OrdinalIgnoreCase),
            [".pdf"] = new HashSet<string>(["application/pdf"], StringComparer.OrdinalIgnoreCase),
        };

    private static void MapKnowledgeDatasets(RouteGroupBuilder api)
    {
        var assigned = api.MapGroup("/knowledge-datasets");
        assigned.MapGet("", async (AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var datasets = await db.KnowledgeDatasets.AsNoTracking()
                .Where(dataset => dataset.OwnerId == currentUser.UserId && dataset.State == KnowledgeDatasetState.Active)
                .OrderByDescending(dataset => dataset.UpdatedAt)
                .ToListAsync(cancellationToken);
            return Results.Ok(await ToKnowledgeDatasetResponsesAsync(datasets, db, cancellationToken));
        });
        assigned.MapGet("/{id:guid}/jobs", async (Guid id, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var exists = await db.KnowledgeDatasets.AsNoTracking().AnyAsync(
                dataset => dataset.Id == id && dataset.OwnerId == currentUser.UserId,
                cancellationToken);
            if (!exists)
            {
                return Results.NotFound();
            }

            var jobs = await db.DatasetIngestionJobs.AsNoTracking()
                .Where(job => job.DatasetId == id && job.OwnerId == currentUser.UserId)
                .OrderByDescending(job => job.UpdatedAt)
                .ToListAsync(cancellationToken);
            return Results.Ok(jobs.Select(ToDatasetIngestionJobResponse));
        });

        var admin = api.MapGroup("/admin/datasets").RequireAuthorization("admin");
        admin.MapGet("", async (bool includeArchived, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var query = db.KnowledgeDatasets.AsNoTracking();
            if (!includeArchived)
            {
                query = query.Where(dataset => dataset.State == KnowledgeDatasetState.Active);
            }

            var datasets = await query.OrderByDescending(dataset => dataset.UpdatedAt).ToListAsync(cancellationToken);
            return Results.Ok(await ToKnowledgeDatasetResponsesAsync(datasets, db, cancellationToken));
        });
        admin.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var dataset = await db.KnowledgeDatasets.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
            return dataset is null ? Results.NotFound() : Results.Ok(await ToKnowledgeDatasetResponseAsync(dataset, db, cancellationToken));
        });
        admin.MapPost("", CreateKnowledgeDatasetAsync);
        admin.MapPut("/{id:guid}", UpdateKnowledgeDatasetAsync);
        admin.MapPost("/{id:guid}/owner", AssignKnowledgeDatasetOwnerAsync);
        admin.MapPost("/{id:guid}/archive", ArchiveKnowledgeDatasetAsync);
        admin.MapPost("/{id:guid}/restore", RestoreKnowledgeDatasetAsync);
        admin.MapGet("/{id:guid}/sources", async (Guid id, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var exists = await db.KnowledgeDatasets.AsNoTracking().AnyAsync(dataset => dataset.Id == id, cancellationToken);
            if (!exists)
            {
                return Results.NotFound();
            }

            var sources = await db.KnowledgeSourceFiles.AsNoTracking()
                .Where(source => source.DatasetId == id)
                .OrderByDescending(source => source.UpdatedAt)
                .ToListAsync(cancellationToken);
            return Results.Ok(sources.Select(ToKnowledgeSourceFileResponse));
        });
        admin.MapPost("/{id:guid}/uploads", IssueDatasetUploadAsync);
        admin.MapPost("/{id:guid}/sources/{sourceId:guid}/finalize", FinalizeDatasetUploadAsync);
        admin.MapGet("/{id:guid}/jobs", async (Guid id, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var exists = await db.KnowledgeDatasets.AsNoTracking().AnyAsync(dataset => dataset.Id == id, cancellationToken);
            if (!exists)
            {
                return Results.NotFound();
            }

            var jobs = await db.DatasetIngestionJobs.AsNoTracking()
                .Where(job => job.DatasetId == id)
                .OrderByDescending(job => job.UpdatedAt)
                .ToListAsync(cancellationToken);
            return Results.Ok(jobs.Select(ToDatasetIngestionJobResponse));
        });
        admin.MapPost("/jobs/{jobId:guid}/retry", RetryDatasetIngestionJobAsync);
    }

    private static async Task<IResult> CreateKnowledgeDatasetAsync(
        CreateKnowledgeDatasetRequest request,
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var owner = await userManager.FindByIdAsync(request.OwnerId.ToString("D", System.Globalization.CultureInfo.InvariantCulture));
        if (owner is null || !owner.IsActive)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["ownerId"] = ["The assigned owner does not exist or is inactive."] });
        }

        try
        {
            var dataset = new KnowledgeDataset(request.OwnerId, request.Name, request.Description);
            db.KnowledgeDatasets.Add(dataset);
            AddAudit(db, dataset.OwnerId, "knowledge_dataset", "created", "knowledge_dataset", dataset.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created("/api/v1/admin/datasets/" + dataset.Id, await ToKnowledgeDatasetResponseAsync(dataset, db, cancellationToken));
        }
        catch (DomainValidationException exception)
        {
            return DomainValidationProblem(exception);
        }
    }

    private static async Task<IResult> UpdateKnowledgeDatasetAsync(
        Guid id,
        UpdateKnowledgeDatasetRequest request,
        AppDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var dataset = await db.KnowledgeDatasets.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        if (dataset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return StaleDatasetConflict();
        }

        try
        {
            dataset.Update(request.Name, request.Description);
            AddAudit(db, dataset.OwnerId, "knowledge_dataset", "updated", "knowledge_dataset", dataset.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToKnowledgeDatasetResponseAsync(dataset, db, cancellationToken));
        }
        catch (DomainValidationException exception)
        {
            return DomainValidationProblem(exception);
        }
    }

    private static async Task<IResult> AssignKnowledgeDatasetOwnerAsync(
        Guid id,
        AssignKnowledgeDatasetOwnerRequest request,
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        var dataset = await db.KnowledgeDatasets.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        if (dataset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return StaleDatasetConflict();
        }

        var newOwner = await userManager.FindByIdAsync(request.OwnerId.ToString("D", System.Globalization.CultureInfo.InvariantCulture));
        if (newOwner is null || !newOwner.IsActive)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["ownerId"] = ["The assigned owner does not exist or is inactive."] });
        }

        var activeJob = await db.DatasetIngestionJobs.AnyAsync(
            job => job.DatasetId == id && (job.State == DatasetIngestionState.Processing || job.State == DatasetIngestionState.WaitingForOcr),
            cancellationToken);
        if (activeJob)
        {
            return Results.Conflict(new { error = "A dataset owner cannot be changed while ingestion is active." });
        }

        var sources = await db.KnowledgeSourceFiles.Where(source => source.DatasetId == id).ToListAsync(cancellationToken);
        var completedSources = sources.Where(source => source.State == KnowledgeSourceFileState.Completed).ToArray();
        var jobs = await db.DatasetIngestionJobs.Where(job => job.DatasetId == id).ToListAsync(cancellationToken);
        dataset.AssignOwner(request.OwnerId);
        foreach (var source in sources)
        {
            source.AssignOwner(request.OwnerId);
        }

        foreach (var job in jobs)
        {
            job.AssignOwner(request.OwnerId);
        }

        // Neo4j retains owner_id as a strict retrieval filter. Requeue every completed
        // source through the worker rather than allowing a relational owner change to
        // leave its derived graph inaccessible or rewrite it from the browser/API.
        foreach (var source in completedSources)
        {
            source.MarkFailed("Requeued after dataset owner reassignment to synchronize the derived graph.");
            source.MarkQueued();
            var reingestion = new DatasetIngestionJob(request.OwnerId, dataset.Id, source.Id);
            db.DatasetIngestionJobs.Add(reingestion);
            db.OutboxMessages.Add(new OutboxMessage(
                request.OwnerId,
                "dataset.ingestion.requested",
                JsonSerializer.Serialize(new DatasetIngestionOutboxEvent(reingestion.Id, dataset.Id, source.Id), JsonOptions)));
        }

        AddAudit(db, request.OwnerId, "knowledge_dataset", "owner_assigned", "knowledge_dataset", dataset.Id);
        if (completedSources.Length > 0)
        {
            AddAudit(db, request.OwnerId, "knowledge_dataset", "owner_graph_resync_queued", "knowledge_dataset", dataset.Id);
        }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await ToKnowledgeDatasetResponseAsync(dataset, db, cancellationToken));
    }

    private static async Task<IResult> ArchiveKnowledgeDatasetAsync(
        Guid id,
        ConcurrencyTokenRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var dataset = await db.KnowledgeDatasets.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        if (dataset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return StaleDatasetConflict();
        }

        var activeJob = await db.DatasetIngestionJobs.AnyAsync(
            job => job.DatasetId == id && (job.State == DatasetIngestionState.Processing || job.State == DatasetIngestionState.WaitingForOcr),
            cancellationToken);
        if (activeJob)
        {
            return Results.Conflict(new { error = "A dataset with active ingestion cannot be archived." });
        }

        dataset.Archive();
        AddAudit(db, dataset.OwnerId, "knowledge_dataset", "archived", "knowledge_dataset", dataset.Id);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await ToKnowledgeDatasetResponseAsync(dataset, db, cancellationToken));
    }

    private static async Task<IResult> RestoreKnowledgeDatasetAsync(
        Guid id,
        ConcurrencyTokenRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var dataset = await db.KnowledgeDatasets.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        if (dataset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return StaleDatasetConflict();
        }

        dataset.Restore();
        AddAudit(db, dataset.OwnerId, "knowledge_dataset", "restored", "knowledge_dataset", dataset.Id);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await ToKnowledgeDatasetResponseAsync(dataset, db, cancellationToken));
    }

    private static async Task<IResult> IssueDatasetUploadAsync(
        Guid id,
        IssueDatasetUploadRequest request,
        AppDbContext db,
        IObjectStorage storage,
        CancellationToken cancellationToken)
    {
        var dataset = await db.KnowledgeDatasets.SingleOrDefaultAsync(value => value.Id == id && value.State == KnowledgeDatasetState.Active, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        var uploadError = ValidateDatasetUpload(request);
        if (uploadError is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [uploadError] });
        }

        try
        {
            var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
            var objectKey = $"datasets/{dataset.Id:N}/{Guid.CreateVersion7():N}/source{extension}";
            var source = new KnowledgeSourceFile(
                dataset.OwnerId,
                dataset.Id,
                request.FileName,
                objectKey,
                NormalizeContentType(request.ContentType),
                request.SizeBytes,
                request.Sha256);
            db.KnowledgeSourceFiles.Add(source);
            await db.SaveChangesAsync(cancellationToken);

            var upload = await storage.CreatePresignedUploadAsync(source.ObjectKey, source.ContentType, TimeSpan.FromMinutes(10), cancellationToken);
            return Results.Ok(new DatasetUploadResponse(
                ToKnowledgeSourceFileResponse(source),
                upload.UploadUrl,
                upload.RequiredHeaders,
                upload.ExpiresAt));
        }
        catch (DomainValidationException exception)
        {
            return DomainValidationProblem(exception);
        }
        catch (Exception)
        {
            return Results.Problem(title: "Dataset upload preparation is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> FinalizeDatasetUploadAsync(
        Guid id,
        Guid sourceId,
        AppDbContext db,
        IObjectStorage storage,
        CancellationToken cancellationToken)
    {
        var dataset = await db.KnowledgeDatasets.SingleOrDefaultAsync(value => value.Id == id && value.State == KnowledgeDatasetState.Active, cancellationToken);
        var source = await db.KnowledgeSourceFiles.SingleOrDefaultAsync(value => value.Id == sourceId && value.DatasetId == id, cancellationToken);
        if (dataset is null || source is null)
        {
            return Results.NotFound();
        }

        if (source.State != KnowledgeSourceFileState.PendingUpload)
        {
            return Results.Conflict(new { error = "This source upload has already been finalized." });
        }

        try
        {
            var verified = await storage.VerifyObjectAsync(
                source.ObjectKey,
                source.ContentType,
                source.ExpectedSizeBytes,
                source.ExpectedSha256,
                cancellationToken);
            source.ConfirmUpload(verified.SizeBytes, verified.Sha256, verified.ContentType);
            source.MarkQueued();
            var job = new DatasetIngestionJob(dataset.OwnerId, dataset.Id, source.Id);
            db.DatasetIngestionJobs.Add(job);
            db.OutboxMessages.Add(new OutboxMessage(
                dataset.OwnerId,
                "dataset.ingestion.requested",
                JsonSerializer.Serialize(new DatasetIngestionOutboxEvent(job.Id, dataset.Id, source.Id), JsonOptions)));
            AddAudit(db, dataset.OwnerId, "knowledge_dataset", "ingestion_queued", "knowledge_source_file", source.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Accepted(
                $"/api/v1/knowledge-datasets/{dataset.Id}/jobs",
                new FinalizeDatasetUploadResponse(ToKnowledgeSourceFileResponse(source), ToDatasetIngestionJobResponse(job)));
        }
        catch (DomainValidationException exception)
        {
            return DomainValidationProblem(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [exception.Message] });
        }
        catch (Exception)
        {
            return Results.Problem(title: "Dataset upload verification is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> RetryDatasetIngestionJobAsync(
        Guid jobId,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var job = await db.DatasetIngestionJobs.SingleOrDefaultAsync(value => value.Id == jobId, cancellationToken);
        if (job is null)
        {
            return Results.NotFound();
        }

        var source = await db.KnowledgeSourceFiles.SingleOrDefaultAsync(value => value.Id == job.SourceFileId && value.OwnerId == job.OwnerId, cancellationToken);
        if (source is null)
        {
            return Results.NotFound();
        }

        try
        {
            if (job.State == DatasetIngestionState.WaitingForOcr)
            {
                // A fresh outbox delivery renews the scoped callback capability. Starting
                // a new OCR attempt is safe because graph writes are deterministic MERGEs.
                job.MarkFailed("Requeued by an administrator to recover a waiting Textract job.");
                source.MarkFailed("Requeued by an administrator to recover a waiting Textract job.");
            }

            job.Retry();
            source.MarkQueued();
            db.OutboxMessages.Add(new OutboxMessage(
                job.OwnerId,
                "dataset.ingestion.requested",
                JsonSerializer.Serialize(new DatasetIngestionOutboxEvent(job.Id, job.DatasetId, job.SourceFileId), JsonOptions)));
            AddAudit(db, job.OwnerId, "knowledge_dataset", "ingestion_retried", "dataset_ingestion_job", job.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Accepted("/api/v1/admin/datasets/jobs/" + job.Id, ToDatasetIngestionJobResponse(job));
        }
        catch (DomainValidationException exception)
        {
            return DomainValidationProblem(exception);
        }
    }

    private static string? ValidateDatasetUpload(IssueDatasetUploadRequest request)
    {
        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        var contentType = NormalizeContentType(request.ContentType);
        if (!DatasetUploadContentTypes.TryGetValue(extension, out var allowedContentTypes) || !allowedContentTypes.Contains(contentType))
        {
            return "Only CSV, JSON, Markdown, TXT, DOCX, and PDF files with an approved content type are allowed.";
        }

        if (request.SizeBytes is < 1 or > 100L * 1024 * 1024)
        {
            return "Dataset source files must be between 1 byte and 100 MB.";
        }

        var normalizedHash = request.Sha256?.Trim() ?? string.Empty;
        return normalizedHash.Length == 64 && normalizedHash.All(Uri.IsHexDigit)
            ? null
            : "A valid SHA-256 checksum is required.";
    }

    private static string NormalizeContentType(string contentType) =>
        contentType?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;

    private static IResult StaleDatasetConflict() =>
        Results.Conflict(new { error = "The dataset was changed by another request. Refresh and retry." });

    private static async Task<IReadOnlyList<KnowledgeDatasetResponse>> ToKnowledgeDatasetResponsesAsync(
        List<KnowledgeDataset> datasets,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (datasets.Count == 0)
        {
            return Array.Empty<KnowledgeDatasetResponse>();
        }

        var ids = datasets.Select(dataset => dataset.Id).ToArray();
        var sourceStates = await db.KnowledgeSourceFiles.AsNoTracking()
            .Where(source => ids.Contains(source.DatasetId))
            .Select(source => new { source.DatasetId, source.State })
            .ToListAsync(cancellationToken);
        var counts = sourceStates.GroupBy(source => source.DatasetId)
            .ToDictionary(
                group => group.Key,
                group => (Total: group.Count(), Completed: group.Count(source => source.State == KnowledgeSourceFileState.Completed)));
        return datasets.Select(dataset => ToKnowledgeDatasetResponse(dataset, counts.GetValueOrDefault(dataset.Id))).ToArray();
    }

    private static async Task<KnowledgeDatasetResponse> ToKnowledgeDatasetResponseAsync(
        KnowledgeDataset dataset,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var sourceStates = await db.KnowledgeSourceFiles.AsNoTracking()
            .Where(source => source.DatasetId == dataset.Id)
            .Select(source => source.State)
            .ToListAsync(cancellationToken);
        return ToKnowledgeDatasetResponse(dataset, (sourceStates.Count, sourceStates.Count(state => state == KnowledgeSourceFileState.Completed)));
    }

    private static KnowledgeDatasetResponse ToKnowledgeDatasetResponse(
        KnowledgeDataset dataset,
        (int Total, int Completed) counts) =>
        new(
            dataset.Id,
            dataset.OwnerId,
            dataset.Name,
            dataset.Description,
            dataset.State,
            counts.Total,
            counts.Completed,
            dataset.ConcurrencyToken,
            dataset.CreatedAt,
            dataset.UpdatedAt);

    private static KnowledgeSourceFileResponse ToKnowledgeSourceFileResponse(KnowledgeSourceFile source) =>
        new(
            source.Id,
            source.DatasetId,
            source.FileName,
            source.ContentType,
            source.ExpectedSizeBytes,
            source.ExpectedSha256,
            source.UploadedSizeBytes,
            source.State,
            source.FailureReason,
            source.CreatedAt,
            source.UpdatedAt);

    private static DatasetIngestionJobResponse ToDatasetIngestionJobResponse(DatasetIngestionJob job) =>
        new(
            job.Id,
            job.DatasetId,
            job.SourceFileId,
            job.State,
            job.AttemptCount,
            job.TextractJobId,
            job.Report,
            job.FailureReason,
            job.CreatedAt,
            job.UpdatedAt);
}

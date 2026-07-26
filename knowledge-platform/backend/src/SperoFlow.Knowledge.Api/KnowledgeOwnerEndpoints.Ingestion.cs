using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Knowledge.Contracts;
using SperoFlow.Knowledge.Domain;
using SperoFlow.Knowledge.Infrastructure;

namespace SperoFlow.Knowledge.Api;

public static partial class KnowledgeEndpoints
{
    private static async Task<IResult> IssueUploadAsync(Guid id, IssueKnowledgeUploadRequest request, HttpContext context, KnowledgeDbContext db, IKnowledgeObjectStorage storage, CancellationToken cancellationToken)
    {
        if (!TryActor(context.User, out var actor))
        {
            return Results.Unauthorized();
        }

        var dataset = await db.Datasets.SingleOrDefaultAsync(value => value.Id == id && value.State == KnowledgeDatasetState.Active, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        if (!CanManage(dataset, actor))
        {
            return Results.Forbid();
        }

        if (dataset.Visibility == KnowledgeVisibility.PendingReview)
        {
            return Results.Conflict(new { error = "Sources cannot change while the dataset is under review." });
        }

        var draftExists = await db.GraphReleases.AsNoTracking().AnyAsync(
            value => value.DatasetId == dataset.Id && value.State == KnowledgeReleaseState.Draft,
            cancellationToken);
        if (draftExists)
        {
            return Results.Conflict(new { error = "Wait for the current graph release to finish before adding a source." });
        }

        var validationError = ValidateUpload(request);
        if (validationError is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [validationError] });
        }

        try
        {
            var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
            var objectKey = $"sources/{dataset.Id:N}/{Guid.CreateVersion7():N}/source{extension}";
            var source = new KnowledgeSourceFile(dataset.Id, dataset.OwnerSubject, request.FileName, objectKey, NormalizeContentType(request.ContentType), request.SizeBytes, request.Sha256);
            db.Sources.Add(source);
            await db.SaveChangesAsync(cancellationToken);
            var upload = await storage.CreatePresignedUploadAsync(source.ObjectKey, source.ContentType, TimeSpan.FromMinutes(10), cancellationToken);
            return Results.Ok(new PresignedKnowledgeUploadResponse(ToSourceResponse(source), upload.UploadUrl, upload.RequiredHeaders, upload.ExpiresAt));
        }
        catch (KnowledgeValidationException exception)
        {
            return ValidationProblem(exception);
        }
        catch (Exception)
        {
            return Results.Problem(title: "Knowledge upload preparation is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> FinalizeUploadAsync(Guid id, Guid sourceId, HttpContext context, KnowledgeDbContext db, IKnowledgeObjectStorage storage, CancellationToken cancellationToken)
    {
        if (!TryActor(context.User, out var actor))
        {
            return Results.Unauthorized();
        }

        var dataset = await db.Datasets.SingleOrDefaultAsync(value => value.Id == id && value.State == KnowledgeDatasetState.Active, cancellationToken);
        var source = await db.Sources.SingleOrDefaultAsync(value => value.Id == sourceId && value.DatasetId == id, cancellationToken);
        if (dataset is null || source is null)
        {
            return Results.NotFound();
        }

        if (!CanManage(dataset, actor))
        {
            return Results.Forbid();
        }

        if (dataset.Visibility == KnowledgeVisibility.PendingReview)
        {
            return Results.Conflict(new { error = "Sources cannot change while the dataset is under review." });
        }

        if (source.State != KnowledgeSourceState.PendingUpload)
        {
            return Results.Conflict(new { error = "This source has already been finalized." });
        }

        var draftExists = await db.GraphReleases.AsNoTracking().AnyAsync(
            value => value.DatasetId == dataset.Id && value.State == KnowledgeReleaseState.Draft,
            cancellationToken);
        if (draftExists)
        {
            return Results.Conflict(new { error = "Wait for the current graph release to finish before finalizing another source." });
        }

        try
        {
            var verified = await storage.VerifyObjectAsync(source.ObjectKey, source.FileName, source.ContentType, source.ExpectedSizeBytes, source.ExpectedSha256, cancellationToken);
            source.ConfirmUpload(verified.SizeBytes, verified.Sha256, verified.ContentType);

            var nonReadySources = await db.Sources
                .Where(value => value.DatasetId == dataset.Id && value.Id != source.Id && value.State != KnowledgeSourceState.Completed && value.State != KnowledgeSourceState.PendingUpload)
                .AnyAsync(cancellationToken);
            if (nonReadySources)
            {
                return Results.Conflict(new { error = "Resolve the existing source ingestion failure before creating a new graph release." });
            }

            source.Queue();
            var release = new KnowledgeGraphRelease(dataset.Id, dataset.OwnerSubject, $"dataset-{dataset.Id:N}-draft-{Guid.CreateVersion7():N}");
            db.GraphReleases.Add(release);

            // Each release is a complete immutable graph snapshot. The new source and every
            // already-completed source get their own job for this release before it can validate.
            var completedSources = await db.Sources
                .Where(value => value.DatasetId == dataset.Id && value.Id != source.Id && value.State == KnowledgeSourceState.Completed)
                .OrderBy(value => value.CreatedAt)
                .ToListAsync(cancellationToken);
            var releaseSources = completedSources.Append(source).ToArray();
            KnowledgeIngestionJob? uploadedSourceJob = null;
            foreach (var releaseSource in releaseSources)
            {
                var job = new KnowledgeIngestionJob(dataset.Id, releaseSource.Id, release.Id, dataset.OwnerSubject);
                db.IngestionJobs.Add(job);
                db.OutboxMessages.Add(new KnowledgeOutboxMessage(
                    dataset.OwnerSubject,
                    "knowledge.ingestion.requested",
                    JsonSerializer.Serialize(new KnowledgeOutboxEvent(job.Id, dataset.Id, releaseSource.Id, release.Id), JsonOptions)));
                if (releaseSource.Id == source.Id)
                {
                    uploadedSourceJob = job;
                }
            }

            AddAudit(db, actor.Subject, "release_snapshot_queued", "knowledge_graph_release", release.Id, $"sources={releaseSources.Length}");
            AddAudit(db, actor.Subject, "source_ingestion_queued", "knowledge_source", source.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Accepted($"/api/v1/knowledge/datasets/{dataset.Id}/jobs", new FinalizeKnowledgeUploadResponse(ToSourceResponse(source), ToJobResponse(uploadedSourceJob!)));
        }
        catch (KnowledgeValidationException exception)
        {
            return ValidationProblem(exception);
        }
        catch (InvalidOperationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [exception.Message] });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { error = "A graph release was created by another request. Refresh and retry." });
        }
        catch (Exception)
        {
            return Results.Problem(title: "Knowledge upload verification is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> ListJobsAsync(Guid id, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryActor(context.User, out var actor))
        {
            return Results.Unauthorized();
        }

        var dataset = await db.Datasets.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        if (!CanManage(dataset, actor))
        {
            return Results.Forbid();
        }

        var jobs = await db.IngestionJobs.AsNoTracking().Where(value => value.DatasetId == id).OrderByDescending(value => value.CreatedAt).ToListAsync(cancellationToken);
        return Results.Ok(jobs.Select(ToJobResponse));
    }

    private static async Task<IResult> RetryJobAsync(Guid jobId, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryActor(context.User, out var actor))
        {
            return Results.Unauthorized();
        }

        var job = await db.IngestionJobs.SingleOrDefaultAsync(value => value.Id == jobId, cancellationToken);
        if (job is null)
        {
            return Results.NotFound();
        }

        var dataset = await db.Datasets.SingleOrDefaultAsync(value => value.Id == job.DatasetId, cancellationToken);
        var source = await db.Sources.SingleOrDefaultAsync(value => value.Id == job.SourceFileId && value.DatasetId == job.DatasetId, cancellationToken);
        var release = await db.GraphReleases.SingleOrDefaultAsync(value => value.Id == job.ReleaseId && value.DatasetId == job.DatasetId, cancellationToken);
        if (dataset is null || source is null || release is null)
        {
            return Results.NotFound();
        }

        if (!CanManage(dataset, actor))
        {
            return Results.Forbid();
        }

        if (release.State is not (KnowledgeReleaseState.Draft or KnowledgeReleaseState.Failed))
        {
            return Results.Conflict(new { error = "Only draft or failed graph releases can be retried." });
        }
        try
        {
            job.Retry();
            if (release.State == KnowledgeReleaseState.Failed)
            {
                release.ReopenForRetry();
                AddAudit(db, actor.Subject, "release_reopened_for_retry", "knowledge_graph_release", release.Id, release.ReleaseKey);
            }

            if (source.State != KnowledgeSourceState.Completed)
            {
                source.RequeueForRetry();
            }

            db.OutboxMessages.Add(new KnowledgeOutboxMessage(dataset.OwnerSubject, "knowledge.ingestion.requested", JsonSerializer.Serialize(new KnowledgeOutboxEvent(job.Id, job.DatasetId, job.SourceFileId, job.ReleaseId), JsonOptions)));
            AddAudit(db, actor.Subject, "source_ingestion_retried", "knowledge_job", job.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Accepted("/api/v1/knowledge/jobs/" + job.Id, ToJobResponse(job));
        }
        catch (KnowledgeValidationException exception)
        {
            return ValidationProblem(exception);
        }
    }
}
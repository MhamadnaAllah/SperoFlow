using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Knowledge.Contracts;
using SperoFlow.Knowledge.Domain;
using SperoFlow.Knowledge.Infrastructure;

namespace SperoFlow.Knowledge.Api;

public static partial class KnowledgeEndpoints
{
    private static async Task<IResult> IssueAccessGrantAsync(KnowledgeAccessGrantRequest request, HttpRequest httpRequest, KnowledgeInternalTokenService tokens, KnowledgeAccessGrantService grants, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryBearer(httpRequest, out var token))
        {
            return Results.Unauthorized();
        }

        var principal = tokens.ValidateMainServiceToken(token, "knowledge.grants");
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var ids = request.DatasetIds.Distinct().ToArray();
        if (string.IsNullOrWhiteSpace(request.Subject) || ids.Length is < 1 or > 20)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["datasetIds"] = ["Select between one and twenty datasets."] });
        }

        var subject = request.Subject.Trim();
        if (!CanActForSubject(principal, subject))
        {
            return Results.Forbid();
        }

        var datasets = await db.Datasets.AsNoTracking()
            .Where(value => ids.Contains(value.Id) && value.State == KnowledgeDatasetState.Active && (value.OwnerSubject == subject || value.Visibility == KnowledgeVisibility.Published))
            .ToListAsync(cancellationToken);
        if (datasets.Count != ids.Length)
        {
            return Results.Forbid();
        }

        var releases = await db.GraphReleases.AsNoTracking()
            .Where(value => ids.Contains(value.DatasetId) && (value.State == KnowledgeReleaseState.Validated || value.State == KnowledgeReleaseState.Published))
            .ToListAsync(cancellationToken);
        var grantsByDataset = new List<KnowledgeGrantDataset>(datasets.Count);
        foreach (var dataset in datasets)
        {
            var release = dataset.Visibility == KnowledgeVisibility.Published
                ? releases.SingleOrDefault(value => value.Id == dataset.PublishedReleaseId && value.State == KnowledgeReleaseState.Published)
                : releases.Where(value => value.DatasetId == dataset.Id).OrderByDescending(value => value.UpdatedAt).FirstOrDefault();
            if (release is null)
            {
                return Results.Forbid();
            }

            grantsByDataset.Add(new KnowledgeGrantDataset(dataset.Id, release.ReleaseKey, dataset.OwnerSubject, dataset.Visibility));
        }

        var issued = grants.Issue(subject, grantsByDataset);
        return Results.Ok(new KnowledgeAccessGrantResponse(issued.Token, issued.ExpiresAt, grantsByDataset.Select(value => value.DatasetId).ToArray()));
    }
    private static async Task<IResult> ListCatalogAsync(string subject, HttpRequest httpRequest, KnowledgeInternalTokenService tokens, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryBearer(httpRequest, out var token))
        {
            return Results.Unauthorized();
        }

        var principal = tokens.ValidateMainServiceToken(token, "knowledge.catalog");
        if (principal is null)
        {
            return Results.Unauthorized();
        }

        var normalized = subject.Trim();
        if (normalized.Length is < 1 or > 256)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["subject"] = ["A valid subject is required."] });
        }

        if (!CanActForSubject(principal, normalized))
        {
            return Results.Forbid();
        }

        var items = await db.Datasets.AsNoTracking()
            .Where(value => value.State == KnowledgeDatasetState.Active && (value.OwnerSubject == normalized || value.Visibility == KnowledgeVisibility.Published))
            .OrderByDescending(value => value.UpdatedAt)
            .Select(value => new KnowledgeCatalogItem(value.Id, value.Name, value.Visibility, value.UpdatedAt))
            .ToListAsync(cancellationToken);
        return Results.Ok(items);
    }
    private static async Task<IResult> GetWorkerJobAsync(Guid id, HttpRequest request, KnowledgeInternalTokenService tokens, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryBearer(request, out var token))
        {
            return Results.Unauthorized();
        }

        var worker = tokens.ValidateWorkerDeliveryToken(token, id);
        if (worker is null)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(request.Headers["X-Knowledge-Worker-Lease-Id"].ToString(), out var requestedLeaseId) || requestedLeaseId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["X-Knowledge-Worker-Lease-Id"] = ["A worker delivery must supply a lease ID."] });
        }

        var job = await db.IngestionJobs.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        var dataset = job is null ? null : await db.Datasets.SingleOrDefaultAsync(value => value.Id == job.DatasetId && value.State == KnowledgeDatasetState.Active, cancellationToken);
        var source = job is null ? null : await db.Sources.SingleOrDefaultAsync(value => value.Id == job.SourceFileId && value.DatasetId == job.DatasetId, cancellationToken);
        var release = job is null ? null : await db.GraphReleases.SingleOrDefaultAsync(value => value.Id == job.ReleaseId, cancellationToken);
        if (job is null || dataset is null || source is null || release is null)
        {
            return Results.NotFound();
        }

        if (IsCompletedDeliveryState(job.State))
        {
            return Results.Conflict(new { error = "This knowledge ingestion delivery has already completed." });
        }

        if (job.State != KnowledgeIngestionState.Processing ||
            !KnowledgeInternalTokenService.MatchesWorkerAttempt(worker, job.AttemptCount))
        {
            return Results.Conflict(new { error = "This knowledge ingestion delivery is stale or has not been dispatched." });
        }

        var now = DateTimeOffset.UtcNow;
        if (!job.TryAcquireLease(requestedLeaseId, now, tokens.WorkerLeaseDuration))
        {
            return Results.Conflict(new { error = "Another worker owns the active lease for this ingestion delivery." });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "The ingestion delivery was claimed by another worker." });
        }

        var leaseExpiresAt = job.LeaseExpiresAt!.Value;
        var executionToken = tokens.CreateWorkerExecutionToken(job.Id, job.AttemptCount, requestedLeaseId, leaseExpiresAt);
        return Results.Ok(new InternalKnowledgeJobResponse(job.Id, dataset.Id, source.Id, release.Id, release.ReleaseKey, dataset.OwnerSubject, dataset.Name, source.ObjectKey, source.FileName, source.ContentType, source.ExpectedSizeBytes, source.ExpectedSha256, job.State, job.TextractJobId, executionToken, leaseExpiresAt));
    }

    private static async Task<IResult> RenewWorkerLeaseAsync(Guid id, HttpRequest request, KnowledgeInternalTokenService tokens, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryBearer(request, out var token))
        {
            return Results.Unauthorized();
        }

        var worker = tokens.ValidateWorkerExecutionToken(token, id);
        if (worker is null || !Guid.TryParse(worker.FindFirst("lease_id")?.Value, out var leaseId))
        {
            return Results.Unauthorized();
        }

        var job = await db.IngestionJobs.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (job is null)
        {
            return Results.NotFound();
        }

        if (!KnowledgeInternalTokenService.MatchesWorkerAttempt(worker, job.AttemptCount) ||
            !job.TryRenewLease(leaseId, DateTimeOffset.UtcNow, tokens.WorkerLeaseDuration))
        {
            return Results.Conflict(new { error = "This knowledge ingestion lease is no longer active." });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "The ingestion lease changed while it was being renewed." });
        }

        var leaseExpiresAt = job.LeaseExpiresAt!.Value;
        return Results.Ok(new KnowledgeWorkerLeaseResponse(tokens.CreateWorkerExecutionToken(job.Id, job.AttemptCount, leaseId, leaseExpiresAt), leaseExpiresAt));
    }    private static async Task<IResult> CompleteWorkerJobAsync(Guid id, CompleteKnowledgeJobRequest request, HttpRequest httpRequest, KnowledgeInternalTokenService tokens, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryBearer(httpRequest, out var token))
        {
            return Results.Unauthorized();
        }

        var worker = tokens.ValidateWorkerExecutionToken(token, id);
        if (worker is null)
        {
            return Results.Unauthorized();
        }

        var workerSubject = worker.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrWhiteSpace(workerSubject))
        {
            return Results.Unauthorized();
        }

        if (!IsJsonObject(request.Report))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["report"] = ["Worker reports must be valid JSON objects."] });
        }

        if (request.ContentUnits < 0 || request.Entities < 0 || request.Facts < 0 || request.Vectors < 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["counters"] = ["Worker counters must be non-negative."] });
        }
        var job = await db.IngestionJobs.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (job is null)
        {
            return Results.NotFound();
        }

        var source = await db.Sources.SingleOrDefaultAsync(value => value.Id == job.SourceFileId && value.DatasetId == job.DatasetId, cancellationToken);
        var release = await db.GraphReleases.SingleOrDefaultAsync(value => value.Id == job.ReleaseId && value.DatasetId == job.DatasetId, cancellationToken);
        if (source is null || release is null)
        {
            return Results.NotFound();
        }

        if (!KnowledgeInternalTokenService.MatchesWorkerAttempt(worker, job.AttemptCount))
        {
            return Results.Conflict(new { error = "This knowledge ingestion callback belongs to a stale delivery attempt." });
        }

        if (IsCompletedDeliveryState(job.State))
        {
            return Results.NoContent();
        }

        if (!Guid.TryParse(worker.FindFirst("lease_id")?.Value, out var leaseId) ||
            !KnowledgeInternalTokenService.MatchesWorkerLease(worker, leaseId) ||
            !job.HasActiveLease(leaseId, DateTimeOffset.UtcNow))
        {
            return Results.Conflict(new { error = "This knowledge ingestion callback belongs to an expired or superseded lease." });
        }

        if (job.State != KnowledgeIngestionState.Processing)
        {
            return Results.Conflict(new { error = "This knowledge ingestion job is not processing." });
        }

        var shouldValidateRelease = false;
        try
        {
            switch (request.State)
            {
                case KnowledgeIngestionState.WaitingForOcr:
                    KnowledgeWorkerReportValidator.ValidateWaitingForOcrReport(request.Report, request.TextractJobId ?? string.Empty);
                    job.MarkWaitingForOcr(request.TextractJobId ?? string.Empty, request.Report);
                    db.OutboxMessages.Add(new KnowledgeOutboxMessage(
                        job.OwnerSubject,
                        "knowledge.ingestion.requested",
                        System.Text.Json.JsonSerializer.Serialize(new KnowledgeOutboxEvent(job.Id, job.DatasetId, job.SourceFileId, job.ReleaseId), JsonOptions),
                        DateTimeOffset.UtcNow.AddSeconds(30)));
                    AddAudit(db, workerSubject, "source_ocr_waiting", "knowledge_job", job.Id);
                    break;
                case KnowledgeIngestionState.Succeeded:
                case KnowledgeIngestionState.SucceededWithWarnings:
                    var report = KnowledgeWorkerReportValidator.ParseSuccessfulReport(
                        request.Report,
                        request.State,
                        release.ReleaseKey,
                        source.UploadedSha256 ?? source.ExpectedSha256);
                    KnowledgeWorkerReportValidator.EnsureCallbackCounters(
                        request.ContentUnits,
                        request.Entities,
                        request.Facts,
                        request.Vectors,
                        report);
                    job.MarkSucceeded(request.Report, request.State == KnowledgeIngestionState.SucceededWithWarnings);
                    if (source.State != KnowledgeSourceState.Completed)
                    {
                        source.MarkCompleted();
                    }

                    shouldValidateRelease = true;
                    AddAudit(db, workerSubject, "source_ingestion_completed", "knowledge_job", job.Id);
                    break;
                case KnowledgeIngestionState.Failed:
                    var reason = request.Error ?? "The knowledge worker reported an unspecified failure.";
                    job.MarkFailed(reason, request.Report);
                    if (source.State != KnowledgeSourceState.Completed)
                    {
                        source.MarkFailed(reason);
                    }

                    if (release.State == KnowledgeReleaseState.Draft)
                    {
                        release.MarkFailed(request.Report);
                    }
                    AddAudit(db, workerSubject, "source_ingestion_failed", "knowledge_job", job.Id, reason);
                    break;
                default:
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["state"] = ["The worker may report waitingForOcr, succeeded, succeededWithWarnings, or failed."] });
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var persistedState = await db.IngestionJobs.AsNoTracking()
                .Where(value => value.Id == id)
                .Select(value => value.State)
                .SingleOrDefaultAsync(cancellationToken);
            return IsCompletedDeliveryState(persistedState)
                ? Results.NoContent()
                : Results.Conflict(new { error = "The knowledge ingestion job changed while this callback was in progress." });
        }
        catch (KnowledgeValidationException exception)
        {
            return ValidationProblem(exception);
        }
        if (shouldValidateRelease)
        {
            try
            {
                await ValidateReleaseIfCompleteAsync(release.Id, workerSubject, db, cancellationToken);
            }
            catch (KnowledgeValidationException exception)
            {
                return ValidationProblem(exception);
            }
        }

        return Results.NoContent();
    }


    private static async Task ValidateReleaseIfCompleteAsync(Guid releaseId, string workerSubject, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        var release = await db.GraphReleases.SingleOrDefaultAsync(value => value.Id == releaseId, cancellationToken);
        if (release is null)
        {
            return;
        }

        await db.Entry(release).ReloadAsync(cancellationToken);
        if (release.State != KnowledgeReleaseState.Draft)
        {
            return;
        }

        var jobs = await db.IngestionJobs
            .Where(value => value.ReleaseId == releaseId)
            .ToListAsync(cancellationToken);
        if (jobs.Count == 0 || jobs.Any(job => !IsSuccessful(job.State)))
        {
            return;
        }

        var sourceIds = jobs.Select(job => job.SourceFileId).Distinct().ToArray();
        var sources = await db.Sources
            .Where(value => sourceIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var report = KnowledgeWorkerReportValidator.BuildReleaseValidationReport(release, jobs, sources);
        release.MarkValidated(report);
        AddAudit(db, workerSubject, "release_validated", "knowledge_graph_release", release.Id, release.ReleaseKey);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var finalState = await db.GraphReleases.AsNoTracking()
                .Where(value => value.Id == releaseId)
                .Select(value => value.State)
                .SingleOrDefaultAsync(cancellationToken);
            if (finalState is not (KnowledgeReleaseState.Validated or KnowledgeReleaseState.Published or KnowledgeReleaseState.Superseded or KnowledgeReleaseState.Failed))
            {
                throw;
            }
        }
    }

    private static bool IsSuccessful(KnowledgeIngestionState state) =>
        state is KnowledgeIngestionState.Succeeded or KnowledgeIngestionState.SucceededWithWarnings;

    private static bool IsCompletedDeliveryState(KnowledgeIngestionState state) =>
        state is KnowledgeIngestionState.WaitingForOcr or KnowledgeIngestionState.Succeeded or KnowledgeIngestionState.SucceededWithWarnings or KnowledgeIngestionState.Failed;
    private static bool CanActForSubject(ClaimsPrincipal principal, string subject) =>
        string.Equals(principal.FindFirst("user_id")?.Value, subject, StringComparison.OrdinalIgnoreCase);
}
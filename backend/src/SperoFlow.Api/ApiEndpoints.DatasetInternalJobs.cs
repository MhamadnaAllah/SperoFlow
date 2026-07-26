using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SperoFlow.Application;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static void MapInternalDatasetJobs(IEndpointRouteBuilder app)
    {
        var internalApi = app.MapGroup("/internal/v1/dataset-jobs");
        internalApi.MapGet("/{id:guid}", async (
            Guid id,
            HttpRequest request,
            IServiceTokenValidator validator,
            IOptions<ServiceJwtOptions> jwt,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var principal = ValidateInternalToken(request, validator, jwt.Value.ApiAudience, "jobs.process");
            if (!HasJobClaim(principal, id))
            {
                return Results.Unauthorized();
            }

            var job = await db.DatasetIngestionJobs.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
            if (job is null)
            {
                return Results.NotFound();
            }

            var source = await db.KnowledgeSourceFiles.SingleOrDefaultAsync(
                value => value.Id == job.SourceFileId && value.DatasetId == job.DatasetId && value.OwnerId == job.OwnerId,
                cancellationToken);
            var dataset = await db.KnowledgeDatasets.SingleOrDefaultAsync(
                value => value.Id == job.DatasetId && value.OwnerId == job.OwnerId && value.State == KnowledgeDatasetState.Active,
                cancellationToken);
            if (source is null || dataset is null)
            {
                return Results.NotFound();
            }

            if (job.State is DatasetIngestionState.Succeeded or DatasetIngestionState.SucceededWithWarnings)
            {
                return Results.Conflict(new { error = "This dataset ingestion job has already completed." });
            }

            if (job.State is DatasetIngestionState.Queued or DatasetIngestionState.Failed or DatasetIngestionState.WaitingForOcr)
            {
                job.MarkProcessing();
                source.MarkProcessing();
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new InternalDatasetIngestionJobResponse(
                job.Id,
                dataset.Id,
                source.Id,
                job.OwnerId,
                dataset.Name,
                source.ObjectKey,
                source.FileName,
                source.ContentType,
                source.ExpectedSizeBytes,
                source.ExpectedSha256,
                job.State,
                job.TextractJobId));
        });

        internalApi.MapPost("/{id:guid}/complete", async (
            Guid id,
            InternalDatasetIngestionCompletionRequest request,
            HttpRequest httpRequest,
            IServiceTokenValidator validator,
            IOptions<ServiceJwtOptions> jwt,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var principal = ValidateInternalToken(httpRequest, validator, jwt.Value.ApiAudience, "jobs.process");
            if (!HasJobClaim(principal, id))
            {
                return Results.Unauthorized();
            }

            if (!IsJsonObject(request.Report))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["report"] = ["Dataset worker reports must be valid JSON objects."] });
            }

            var job = await db.DatasetIngestionJobs.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
            if (job is null)
            {
                return Results.NotFound();
            }

            var source = await db.KnowledgeSourceFiles.SingleOrDefaultAsync(
                value => value.Id == job.SourceFileId && value.DatasetId == job.DatasetId && value.OwnerId == job.OwnerId,
                cancellationToken);
            if (source is null)
            {
                return Results.NotFound();
            }

            switch (request.State)
            {
                case DatasetIngestionState.WaitingForOcr:
                    try
                    {
                        job.MarkWaitingForOcr(request.TextractJobId ?? string.Empty);
                    }
                    catch (DomainValidationException exception)
                    {
                        return DomainValidationProblem(exception);
                    }
                    AddAudit(db, job.OwnerId, "knowledge_dataset", "ocr_waiting", "dataset_ingestion_job", job.Id);
                    break;
                case DatasetIngestionState.Succeeded:
                    job.MarkSucceeded(request.Report);
                    source.MarkCompleted();
                    AddAudit(db, job.OwnerId, "knowledge_dataset", "ingestion_completed", "dataset_ingestion_job", job.Id);
                    break;
                case DatasetIngestionState.SucceededWithWarnings:
                    job.MarkSucceededWithWarnings(request.Report);
                    source.MarkCompleted();
                    AddAudit(db, job.OwnerId, "knowledge_dataset", "ingestion_completed_with_warnings", "dataset_ingestion_job", job.Id);
                    break;
                case DatasetIngestionState.Failed:
                    var reason = request.Error ?? "The AI worker reported an unspecified dataset ingestion failure.";
                    job.MarkFailed(reason, request.Report);
                    source.MarkFailed(reason);
                    AddAudit(db, job.OwnerId, "knowledge_dataset", "ingestion_failed", "dataset_ingestion_job", job.Id);
                    break;
                default:
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["state"] = ["The worker may report waitingForOcr, succeeded, succeededWithWarnings, or failed."] });
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static bool IsJsonObject(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
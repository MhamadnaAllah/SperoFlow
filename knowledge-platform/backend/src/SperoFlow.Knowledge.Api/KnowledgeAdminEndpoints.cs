using Microsoft.EntityFrameworkCore;
using SperoFlow.Knowledge.Contracts;
using SperoFlow.Knowledge.Domain;
using SperoFlow.Knowledge.Infrastructure;

namespace SperoFlow.Knowledge.Api;

public static partial class KnowledgeEndpoints
{
    private static async Task<IResult> ListAdminDatasetsAsync(bool includeArchived, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        var query = db.Datasets.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(value => value.State != KnowledgeDatasetState.Archived);
        }

        var datasets = await query.OrderByDescending(value => value.UpdatedAt).ToListAsync(cancellationToken);
        return Results.Ok(await ToDatasetResponsesAsync(datasets, db, cancellationToken));
    }

    private static async Task<IResult> AssignOwnerAsync(Guid id, AssignKnowledgeOwnerRequest request, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryActor(context.User, out var actor))
        {
            return Results.Unauthorized();
        }

        var dataset = await db.Datasets.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        if (dataset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return StaleConflict();
        }

        var hasSources = await db.Sources.AnyAsync(value => value.DatasetId == id, cancellationToken);
        if (hasSources)
        {
            return Results.Conflict(new { error = "Owner assignment is frozen after a source is uploaded; create a new dataset to preserve provenance." });
        }

        try
        {
            dataset.AssignOwner(request.OwnerSubject);
            AddAudit(db, actor.Subject, "dataset_owner_assigned", "knowledge_dataset", dataset.Id, request.OwnerSubject);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToDatasetResponseAsync(dataset, db, cancellationToken));
        }
        catch (KnowledgeValidationException exception)
        {
            return ValidationProblem(exception);
        }
    }

    private static async Task<IResult> PublishDatasetAsync(Guid id, PublishKnowledgeDatasetRequest request, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryActor(context.User, out var actor))
        {
            return Results.Unauthorized();
        }

        var dataset = await db.Datasets.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        var release = await db.GraphReleases.SingleOrDefaultAsync(value => value.Id == request.ReleaseId && value.DatasetId == id, cancellationToken);
        if (dataset is null || release is null)
        {
            return Results.NotFound();
        }

        if (dataset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return StaleConflict();
        }

        var draftExists = await db.GraphReleases.AsNoTracking().AnyAsync(
            value => value.DatasetId == id && value.State == KnowledgeReleaseState.Draft,
            cancellationToken);
        if (draftExists)
        {
            return Results.Conflict(new { error = "A graph release is still in progress." });
        }

        try
        {
            var current = await db.GraphReleases.Where(value => value.DatasetId == id && value.State == KnowledgeReleaseState.Published).ToListAsync(cancellationToken);
            foreach (var previous in current)
            {
                previous.Supersede();
            }

            release.Publish();
            dataset.Publish(release.Id);
            AddAudit(db, actor.Subject, "dataset_published", "knowledge_dataset", dataset.Id, release.ReleaseKey);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToDatasetResponseAsync(dataset, db, cancellationToken));
        }
        catch (KnowledgeValidationException exception)
        {
            return ValidationProblem(exception);
        }
    }

    private static async Task<IResult> ArchiveDatasetAsync(Guid id, ConcurrencyTokenRequest request, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryActor(context.User, out var actor))
        {
            return Results.Unauthorized();
        }

        var dataset = await db.Datasets.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        if (dataset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return StaleConflict();
        }

        var active = await db.IngestionJobs.AnyAsync(value => value.DatasetId == id && (value.State == KnowledgeIngestionState.Processing || value.State == KnowledgeIngestionState.WaitingForOcr), cancellationToken);
        if (active)
        {
            return Results.Conflict(new { error = "An active ingestion job prevents archival." });
        }

        dataset.Archive();
        AddAudit(db, actor.Subject, "dataset_archived", "knowledge_dataset", dataset.Id);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await ToDatasetResponseAsync(dataset, db, cancellationToken));
    }

    private static async Task<IResult> RestoreDatasetAsync(Guid id, ConcurrencyTokenRequest request, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryActor(context.User, out var actor))
        {
            return Results.Unauthorized();
        }

        var dataset = await db.Datasets.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (dataset is null)
        {
            return Results.NotFound();
        }

        if (dataset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return StaleConflict();
        }

        dataset.Restore();
        AddAudit(db, actor.Subject, "dataset_restored", "knowledge_dataset", dataset.Id);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await ToDatasetResponseAsync(dataset, db, cancellationToken));
    }

    private static async Task<IResult> ListReleasesAsync(Guid id, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        var releases = await db.GraphReleases.AsNoTracking().Where(value => value.DatasetId == id).OrderByDescending(value => value.CreatedAt).ToListAsync(cancellationToken);
        return Results.Ok(releases.Select(ToReleaseResponse));
    }
}
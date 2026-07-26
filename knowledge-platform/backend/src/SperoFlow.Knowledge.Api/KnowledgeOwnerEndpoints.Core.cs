using Microsoft.EntityFrameworkCore;
using SperoFlow.Knowledge.Contracts;
using SperoFlow.Knowledge.Domain;
using SperoFlow.Knowledge.Infrastructure;

namespace SperoFlow.Knowledge.Api;

public static partial class KnowledgeEndpoints
{
    private static async Task<IResult> ListVisibleDatasetsAsync(HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryActor(context.User, out var actor))
        {
            return Results.Unauthorized();
        }

        var datasets = await db.Datasets.AsNoTracking()
            .Where(value => value.State == KnowledgeDatasetState.Active && (value.OwnerSubject == actor.Subject || value.Visibility == KnowledgeVisibility.Published))
            .OrderByDescending(value => value.UpdatedAt)
            .ToListAsync(cancellationToken);
        return Results.Ok(await ToDatasetResponsesAsync(datasets, db, cancellationToken));
    }

    private static async Task<IResult> CreateDatasetAsync(CreateKnowledgeDatasetRequest request, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (!TryActor(context.User, out var actor))
        {
            return Results.Unauthorized();
        }

        try
        {
            var dataset = new KnowledgeDataset(actor.Subject, request.Name, request.Description);
            db.Datasets.Add(dataset);
            AddAudit(db, actor.Subject, "dataset_created", "knowledge_dataset", dataset.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/knowledge/datasets/{dataset.Id}", await ToDatasetResponseAsync(dataset, db, cancellationToken));
        }
        catch (KnowledgeValidationException exception)
        {
            return ValidationProblem(exception);
        }
    }

    private static async Task<IResult> GetDatasetAsync(Guid id, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
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

        if (!CanManage(dataset, actor) && dataset.Visibility != KnowledgeVisibility.Published)
        {
            return Results.Forbid();
        }

        return Results.Ok(await ToDatasetResponseAsync(dataset, db, cancellationToken));
    }

    private static async Task<IResult> UpdateDatasetAsync(Guid id, UpdateKnowledgeDatasetRequest request, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
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

        if (!CanManage(dataset, actor))
        {
            return Results.Forbid();
        }

        if (dataset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return StaleConflict();
        }

        try
        {
            dataset.Update(request.Name, request.Description);
            AddAudit(db, actor.Subject, "dataset_updated", "knowledge_dataset", dataset.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToDatasetResponseAsync(dataset, db, cancellationToken));
        }
        catch (KnowledgeValidationException exception)
        {
            return ValidationProblem(exception);
        }
    }

    private static async Task<IResult> SubmitForReviewAsync(Guid id, ConcurrencyTokenRequest request, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
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

        if (!CanManage(dataset, actor))
        {
            return Results.Forbid();
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
            return Results.Conflict(new { error = "Wait for the complete graph release before submitting this dataset for review." });
        }

        var latestRelease = await db.GraphReleases.AsNoTracking()
            .Where(value => value.DatasetId == id)
            .OrderByDescending(value => value.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestRelease?.State != KnowledgeReleaseState.Validated)
        {
            return Results.Conflict(new { error = "The latest complete graph release must validate before review." });
        }

        try
        {
            dataset.SubmitForReview();
            AddAudit(db, actor.Subject, "dataset_submitted_for_review", "knowledge_dataset", dataset.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToDatasetResponseAsync(dataset, db, cancellationToken));
        }
        catch (KnowledgeValidationException exception)
        {
            return ValidationProblem(exception);
        }
    }

    private static async Task<IResult> ReturnToPrivateAsync(Guid id, ConcurrencyTokenRequest request, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
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

        if (!CanManage(dataset, actor))
        {
            return Results.Forbid();
        }

        if (dataset.ConcurrencyToken != request.ConcurrencyToken)
        {
            return StaleConflict();
        }

        try
        {
            dataset.ReturnToPrivate();
            AddAudit(db, actor.Subject, "dataset_returned_to_private", "knowledge_dataset", dataset.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToDatasetResponseAsync(dataset, db, cancellationToken));
        }
        catch (KnowledgeValidationException exception)
        {
            return ValidationProblem(exception);
        }
    }

    private static async Task<IResult> ListSourcesAsync(Guid id, HttpContext context, KnowledgeDbContext db, CancellationToken cancellationToken)
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

        var sources = await db.Sources.AsNoTracking().Where(value => value.DatasetId == id).OrderByDescending(value => value.CreatedAt).ToListAsync(cancellationToken);
        return Results.Ok(sources.Select(ToSourceResponse));
    }
}
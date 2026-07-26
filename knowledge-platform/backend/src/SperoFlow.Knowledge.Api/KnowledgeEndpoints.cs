using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Knowledge.Contracts;
using SperoFlow.Knowledge.Domain;
using SperoFlow.Knowledge.Infrastructure;

namespace SperoFlow.Knowledge.Api;

public static partial class KnowledgeEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, IReadOnlySet<string>> UploadContentTypes =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [".csv"] = new HashSet<string>(["text/csv", "application/csv", "application/vnd.ms-excel"], StringComparer.OrdinalIgnoreCase),
            [".json"] = new HashSet<string>(["application/json", "text/json"], StringComparer.OrdinalIgnoreCase),
            [".md"] = new HashSet<string>(["text/markdown", "text/plain"], StringComparer.OrdinalIgnoreCase),
            [".txt"] = new HashSet<string>(["text/plain"], StringComparer.OrdinalIgnoreCase),
            [".docx"] = new HashSet<string>(["application/vnd.openxmlformats-officedocument.wordprocessingml.document"], StringComparer.OrdinalIgnoreCase),
            [".pdf"] = new HashSet<string>(["application/pdf"], StringComparer.OrdinalIgnoreCase),
        };

    public static void Map(WebApplication app)
    {
        var owner = app.MapGroup("/api/v1/knowledge")
            .RequireAuthorization("knowledge-owner")
            .RequireRateLimiting("portal")
            .AddEndpointFilter<KnowledgeAntiforgeryValidationFilter>();
        owner.MapGet("/datasets", ListVisibleDatasetsAsync);
        owner.MapPost("/datasets", CreateDatasetAsync);
        owner.MapGet("/datasets/{id:guid}", GetDatasetAsync);
        owner.MapPut("/datasets/{id:guid}", UpdateDatasetAsync);
        owner.MapPost("/datasets/{id:guid}/submit-review", SubmitForReviewAsync);
        owner.MapPost("/datasets/{id:guid}/return-to-private", ReturnToPrivateAsync);
        owner.MapGet("/datasets/{id:guid}/sources", ListSourcesAsync);
        owner.MapPost("/datasets/{id:guid}/uploads", IssueUploadAsync);
        owner.MapPost("/datasets/{id:guid}/sources/{sourceId:guid}/finalize", FinalizeUploadAsync);
        owner.MapGet("/datasets/{id:guid}/jobs", ListJobsAsync);
        owner.MapPost("/jobs/{jobId:guid}/retry", RetryJobAsync);

        var admin = app.MapGroup("/api/v1/admin/knowledge")
            .RequireAuthorization("knowledge-admin")
            .RequireRateLimiting("portal")
            .AddEndpointFilter<KnowledgeAntiforgeryValidationFilter>();
        admin.MapGet("/datasets", ListAdminDatasetsAsync);
        admin.MapPost("/datasets/{id:guid}/owner", AssignOwnerAsync);
        admin.MapPost("/datasets/{id:guid}/publish", PublishDatasetAsync);
        admin.MapPost("/datasets/{id:guid}/archive", ArchiveDatasetAsync);
        admin.MapPost("/datasets/{id:guid}/restore", RestoreDatasetAsync);
        admin.MapGet("/datasets/{id:guid}/releases", ListReleasesAsync);

        var internalApi = app.MapGroup("/internal/v1/knowledge");
        internalApi.MapPost("/access-grants", IssueAccessGrantAsync);
        internalApi.MapGet("/catalog/{subject}", ListCatalogAsync);
        internalApi.MapGet("/jobs/{id:guid}", GetWorkerJobAsync);
        internalApi.MapPost("/jobs/{id:guid}/heartbeat", RenewWorkerLeaseAsync);
        internalApi.MapPost("/jobs/{id:guid}/complete", CompleteWorkerJobAsync);
    }

    private static bool TryActor(ClaimsPrincipal principal, out KnowledgeActor actor)
    {
        try
        {
            actor = KnowledgeActor.FromPrincipal(principal);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            actor = null!;
            return false;
        }
    }

    private static bool CanManage(KnowledgeDataset dataset, KnowledgeActor actor) =>
        actor.IsAdmin || string.Equals(dataset.OwnerSubject, actor.Subject, StringComparison.Ordinal);

    private static void AddAudit(KnowledgeDbContext db, string actorSubject, string action, string entityType, Guid entityId, string? detail = null) =>
        db.AuditEvents.Add(new KnowledgeAuditEvent(actorSubject, action, entityType, entityId, detail));

    private static string? ValidateUpload(IssueKnowledgeUploadRequest request)
    {
        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        var type = NormalizeContentType(request.ContentType);
        if (!UploadContentTypes.TryGetValue(extension, out var allowed) || !allowed.Contains(type))
        {
            return "Only CSV, JSON, Markdown, TXT, DOCX, and PDF files with an approved content type are allowed.";
        }

        if (request.SizeBytes is < 1 or > 100L * 1024 * 1024)
        {
            return "Knowledge sources must be between 1 byte and 100 MB.";
        }

        return request.Sha256?.Length == 64 && request.Sha256.All(Uri.IsHexDigit) ? null : "A valid SHA-256 checksum is required.";
    }

    private static string NormalizeContentType(string value) => value?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;

    private static bool TryBearer(HttpRequest request, out string token)
    {
        token = string.Empty;
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = authorization["Bearer ".Length..].Trim();
        return token.Length > 0;
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

    private static IResult ValidationProblem(Exception exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [exception.Message] });

    private static IResult StaleConflict() => Results.Conflict(new { error = "The dataset changed in another request. Refresh and retry." });

    private static KnowledgeSourceResponse ToSourceResponse(KnowledgeSourceFile source) =>
        new(source.Id, source.DatasetId, source.FileName, source.ContentType, source.ExpectedSizeBytes, source.ExpectedSha256, source.State, source.FailureReason, source.CreatedAt, source.UpdatedAt);

    private static KnowledgeJobResponse ToJobResponse(KnowledgeIngestionJob job) =>
        new(job.Id, job.DatasetId, job.SourceFileId, job.ReleaseId, job.State, job.AttemptCount, job.TextractJobId, job.Report, job.FailureReason, job.CreatedAt, job.UpdatedAt);

    private static KnowledgeReleaseResponse ToReleaseResponse(KnowledgeGraphRelease release) =>
        new(release.Id, release.DatasetId, release.ReleaseKey, release.State, release.ValidationReport, release.PublishedAt, release.CreatedAt, release.UpdatedAt);

    private static async Task<IReadOnlyList<KnowledgeDatasetResponse>> ToDatasetResponsesAsync(List<KnowledgeDataset> datasets, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        if (datasets.Count == 0)
        {
            return [];
        }

        var ids = datasets.Select(value => value.Id).ToArray();
        var states = await db.Sources.AsNoTracking().Where(value => ids.Contains(value.DatasetId)).Select(value => new { value.DatasetId, value.State }).ToListAsync(cancellationToken);
        var counts = states.GroupBy(value => value.DatasetId).ToDictionary(group => group.Key, group => (Total: group.Count(), Completed: group.Count(value => value.State == KnowledgeSourceState.Completed)));
        return datasets.Select(value => ToDatasetResponse(value, counts.GetValueOrDefault(value.Id))).ToArray();
    }

    private static async Task<KnowledgeDatasetResponse> ToDatasetResponseAsync(KnowledgeDataset dataset, KnowledgeDbContext db, CancellationToken cancellationToken)
    {
        var states = await db.Sources.AsNoTracking().Where(value => value.DatasetId == dataset.Id).Select(value => value.State).ToListAsync(cancellationToken);
        return ToDatasetResponse(dataset, (states.Count, states.Count(value => value == KnowledgeSourceState.Completed)));
    }

    private static KnowledgeDatasetResponse ToDatasetResponse(KnowledgeDataset dataset, (int Total, int Completed) counts) =>
        new(dataset.Id, dataset.OwnerSubject, dataset.Name, dataset.Description, dataset.State, dataset.Visibility, dataset.PublishedReleaseId, counts.Total, counts.Completed, dataset.ConcurrencyToken, dataset.CreatedAt, dataset.UpdatedAt);
}
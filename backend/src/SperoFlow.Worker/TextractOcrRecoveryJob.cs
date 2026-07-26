using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quartz;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Worker;

/// <summary>
/// Creates fresh, short-lived worker capabilities for Textract jobs that are
/// still waiting. The Python worker consumes the SQS completion signal and
/// polls the matching job; the scheduler prevents a Redis pending entry or an
/// expired callback token from stranding OCR recovery.
/// </summary>
[DisallowConcurrentExecution]
public sealed partial class TextractOcrRecoveryJob(IServiceScopeFactory scopeFactory, ILogger<TextractOcrRecoveryJob> logger) : IJob
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task Execute(IJobExecutionContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-30);
        var waiting = await db.DatasetIngestionJobs
            .Where(job => job.State == DatasetIngestionState.WaitingForOcr)
            .Where(job => job.TextractJobId != null && job.UpdatedAt <= cutoff)
            .OrderBy(job => job.UpdatedAt)
            .Take(20)
            .ToListAsync(context.CancellationToken);

        var requeued = 0;
        foreach (var job in waiting)
        {
            var source = await db.KnowledgeSourceFiles.SingleOrDefaultAsync(
                value => value.Id == job.SourceFileId && value.DatasetId == job.DatasetId && value.OwnerId == job.OwnerId,
                context.CancellationToken);
            var dataset = await db.KnowledgeDatasets.SingleOrDefaultAsync(
                value => value.Id == job.DatasetId && value.OwnerId == job.OwnerId && value.State == KnowledgeDatasetState.Active,
                context.CancellationToken);
            if (source is null || dataset is null)
            {
                job.MarkFailed("The dataset or source is no longer active for Textract recovery.");
                if (source is not null)
                {
                    source.MarkFailed("The dataset is no longer active for Textract recovery.");
                }
                continue;
            }

            // This is the durable retry lease. It keeps the same Textract job ID while
            // refreshing UpdatedAt so only one recovery delivery is issued per interval.
            job.MarkWaitingForOcr(job.TextractJobId!);
            db.OutboxMessages.Add(new OutboxMessage(
                job.OwnerId,
                "dataset.ingestion.requested",
                JsonSerializer.Serialize(new DatasetIngestionOutboxEvent(job.Id, job.DatasetId, job.SourceFileId), SerializerOptions)));
            requeued++;
        }

        if (waiting.Count > 0)
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        if (requeued > 0)
        {
            LogOcrRecoveriesQueued(logger, requeued);
        }
    }

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Queued {JobCount} waiting Textract recovery jobs.")]
    private static partial void LogOcrRecoveriesQueued(ILogger logger, int jobCount);
}

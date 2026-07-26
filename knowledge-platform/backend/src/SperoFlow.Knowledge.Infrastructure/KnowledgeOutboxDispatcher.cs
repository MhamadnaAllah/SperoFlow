using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using SperoFlow.Knowledge.Contracts;
using SperoFlow.Knowledge.Domain;

namespace SperoFlow.Knowledge.Infrastructure;

public sealed class KnowledgeOutboxDispatcher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly KnowledgeInternalTokenService _tokens;
    private readonly KnowledgeRedisOptions _options;

    public KnowledgeOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer redis,
        KnowledgeInternalTokenService tokens,
        IOptions<KnowledgeRedisOptions> options)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _tokens = tokens;
        _options = options.Value;
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        var pending = await db.OutboxMessages
            .Where(message => message.DispatchedAt == null && message.AvailableAt <= DateTimeOffset.UtcNow)
            .OrderBy(message => message.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }

        var stream = _redis.GetDatabase();
        foreach (var message in pending)
        {
            message.MarkAttempted();
            if (!string.Equals(message.Type, "knowledge.ingestion.requested", StringComparison.Ordinal))
            {
                message.MarkDispatched();
                continue;
            }

            var payload = JsonSerializer.Deserialize<KnowledgeOutboxEvent>(message.Payload, SerializerOptions);
            if (payload is null)
            {
                message.MarkDispatched();
                continue;
            }

            var job = await db.IngestionJobs.SingleOrDefaultAsync(
                value => value.Id == payload.JobId &&
                    value.DatasetId == payload.DatasetId &&
                    value.SourceFileId == payload.SourceId &&
                    value.ReleaseId == payload.ReleaseId,
                cancellationToken);
            if (job is null ||
                job.State is KnowledgeIngestionState.Succeeded or KnowledgeIngestionState.SucceededWithWarnings or KnowledgeIngestionState.Failed)
            {
                message.MarkDispatched();
                continue;
            }

            var source = await db.Sources.SingleOrDefaultAsync(
                value => value.Id == job.SourceFileId &&
                    value.DatasetId == job.DatasetId &&
                    value.OwnerSubject == job.OwnerSubject,
                cancellationToken);
            if (source is null)
            {
                message.MarkDispatched();
                continue;
            }

            if (job.State != KnowledgeIngestionState.Processing)
            {
                job.MarkProcessing(DateTimeOffset.UtcNow.Add(_tokens.WorkerDeliveryTokenLifetime));
                if (source.State is KnowledgeSourceState.Queued or KnowledgeSourceState.Failed)
                {
                    source.MarkProcessing();
                }

                // The attempt must be durable before a worker receives its signed delivery.
                await db.SaveChangesAsync(cancellationToken);
            }

            await stream.StreamAddAsync(
                _options.JobsStream,
                [
                    new NameValueEntry("type", message.Type),
                    new NameValueEntry("job_id", job.Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture)),
                    new NameValueEntry("delivery_token", _tokens.CreateWorkerDeliveryToken(job.Id, job.AttemptCount)),
                ]);
            message.MarkDispatched();
        }

        await db.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }

    public async Task<int> RecoverExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        var now = DateTimeOffset.UtcNow;
        var expired = await db.IngestionJobs
            .Where(value => value.State == KnowledgeIngestionState.Processing &&
                ((value.LeaseExpiresAt != null && value.LeaseExpiresAt <= now) ||
                 (value.LeaseExpiresAt == null && value.DispatchExpiresAt != null && value.DispatchExpiresAt <= now)))
            .OrderBy(value => value.UpdatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        var recovered = 0;
        foreach (var job in expired)
        {
            if (!job.RequeueExpiredLease(now))
            {
                continue;
            }

            var source = await db.Sources.SingleOrDefaultAsync(
                value => value.Id == job.SourceFileId && value.DatasetId == job.DatasetId,
                cancellationToken);
            if (source?.State == KnowledgeSourceState.Processing)
            {
                source.RequeueForRetry();
            }

            db.OutboxMessages.Add(new KnowledgeOutboxMessage(
                job.OwnerSubject,
                "knowledge.ingestion.requested",
                JsonSerializer.Serialize(new KnowledgeOutboxEvent(job.Id, job.DatasetId, job.SourceFileId, job.ReleaseId), SerializerOptions)));
            db.AuditEvents.Add(new KnowledgeAuditEvent(
                "service:knowledge-outbox",
                "ingestion_lease_expired_requeued",
                "knowledge_job",
                job.Id,
                "The worker lease or delivery deadline expired before completion."));
            recovered++;
        }

        if (recovered > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return recovered;
    }}
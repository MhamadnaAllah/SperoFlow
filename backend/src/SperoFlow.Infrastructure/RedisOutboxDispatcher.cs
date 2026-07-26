using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using SperoFlow.Application;
using SperoFlow.Contracts;

namespace SperoFlow.Infrastructure;

public sealed class RedisOutboxDispatcher : IOutboxDispatcher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceTokenFactory _tokenFactory;
    private readonly RedisOptions _redisOptions;
    private readonly ServiceJwtOptions _jwtOptions;
    private readonly LegacyKnowledgeIngestionOptions _legacyKnowledgeIngestionOptions;

    public RedisOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer redis,
        IServiceTokenFactory tokenFactory,
        IOptions<RedisOptions> redisOptions,
        IOptions<ServiceJwtOptions> jwtOptions,
        IOptions<LegacyKnowledgeIngestionOptions> legacyKnowledgeIngestionOptions)
    {
        _scopeFactory = scopeFactory;
        _redis = redis;
        _tokenFactory = tokenFactory;
        _redisOptions = redisOptions.Value;
        _jwtOptions = jwtOptions.Value;
        _legacyKnowledgeIngestionOptions = legacyKnowledgeIngestionOptions.Value;
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.OutboxMessages
            .Where(message => message.DispatchedAt == null)
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
            if (string.Equals(message.Type, "dataset.ingestion.requested", StringComparison.Ordinal) && !_legacyKnowledgeIngestionOptions.Enabled)
            {
                // The isolated knowledge platform owns this workflow after cutover. Preserve
                // the historical rows, but never send a legacy dataset job to ai-worker.
                message.MarkDispatched();
                continue;
            }

            Guid? jobId = message.Type switch
            {
                "document.ingestion.requested" => JsonSerializer.Deserialize<IngestionOutboxEvent>(message.Payload, SerializerOptions)?.JobId,
                "dataset.ingestion.requested" => JsonSerializer.Deserialize<DatasetIngestionOutboxEvent>(message.Payload, SerializerOptions)?.JobId,
                _ => null,
            };
            if (!jobId.HasValue)
            {
                message.MarkDispatched();
                continue;
            }

            var callbackToken = _tokenFactory.CreateToken(
                _jwtOptions.ApiAudience,
                "jobs.process",
                message.OwnerId,
                TimeSpan.FromHours(24),
                new Dictionary<string, string>
                {
                    ["job_id"] = jobId.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
                });

            await stream.StreamAddAsync(
                _redisOptions.AiJobsStream,
                [
                    new NameValueEntry("type", message.Type),
                    new NameValueEntry("message_id", message.Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture)),
                    new NameValueEntry("job_id", jobId.Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture)),
                    new NameValueEntry("callback_token", callbackToken),
                ]);
            message.MarkDispatched();
        }

        await db.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }
}

public sealed record IngestionOutboxEvent(Guid JobId, Guid DocumentId);

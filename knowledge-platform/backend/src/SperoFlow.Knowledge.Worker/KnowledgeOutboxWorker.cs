using SperoFlow.Knowledge.Infrastructure;

namespace SperoFlow.Knowledge.Worker;

public sealed partial class KnowledgeOutboxWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(10);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KnowledgeOutboxWorker> _logger;

    public KnowledgeOutboxWorker(IServiceScopeFactory scopeFactory, ILogger<KnowledgeOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<KnowledgeOutboxDispatcher>();
                var recovered = await dispatcher.RecoverExpiredLeasesAsync(stoppingToken);
                var dispatched = await dispatcher.DispatchPendingAsync(stoppingToken);
                if (recovered > 0)
                {
                    LogRecovered(_logger, recovered);
                }
                if (dispatched > 0)
                {
                    LogDispatched(_logger, dispatched);
                    continue;
                }

                await Task.Delay(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogDispatchFailure(_logger, exception);
                await Task.Delay(FailureDelay, stoppingToken);
            }
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Dispatched {MessageCount} knowledge outbox message(s).")]
    private static partial void LogDispatched(ILogger logger, int messageCount);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Requeued {JobCount} expired knowledge ingestion lease(s).")]
    private static partial void LogRecovered(ILogger logger, int jobCount);
    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Knowledge outbox dispatch failed; retrying after a short delay.")]
    private static partial void LogDispatchFailure(ILogger logger, Exception exception);
}
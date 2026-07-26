using Microsoft.EntityFrameworkCore;
using Quartz;
using SperoFlow.Application;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Worker;

[DisallowConcurrentExecution]
public sealed partial class OutboxDispatchJob(IOutboxDispatcher dispatcher, ILogger<OutboxDispatchJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var dispatched = await dispatcher.DispatchPendingAsync(context.CancellationToken);
        if (dispatched > 0)
        {
            LogOutboxMessagesDispatched(logger, dispatched);
        }
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Dispatched {MessageCount} durable outbox messages.")]
    private static partial void LogOutboxMessagesDispatched(ILogger logger, int messageCount);
}

[DisallowConcurrentExecution]
public sealed partial class ReminderSweepJob(IServiceScopeFactory scopeFactory, ILogger<ReminderSweepJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var due = await db.Tasks
            .Where(task => task.State == TaskState.Todo || task.State == TaskState.InProgress)
            .Where(task => task.ReminderAt != null && task.ReminderAt <= now && task.ReminderAt > now.AddMinutes(-5))
            .ToListAsync(context.CancellationToken);

        var created = 0;
        foreach (var task in due)
        {
            var sourceKey = "task-reminder:" + task.Id.ToString("N", System.Globalization.CultureInfo.InvariantCulture) + ":" + task.ReminderAt!.Value.ToUnixTimeSeconds();
            var exists = await db.Notifications.AnyAsync(
                notification => notification.OwnerId == task.OwnerId && notification.SourceKey == sourceKey,
                context.CancellationToken);
            if (exists)
            {
                continue;
            }

            db.Notifications.Add(new InAppNotification(
                task.OwnerId,
                "reminder",
                "Task reminder",
                task.Title,
                sourceKey));
            db.AuditEvents.Add(new AuditEvent(task.OwnerId, "reminder", "created", "task", task.Id, "{}"));
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(context.CancellationToken);
            LogInAppRemindersCreated(logger, created);
        }
    }

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Created {NotificationCount} in-app reminders.")]
    private static partial void LogInAppRemindersCreated(ILogger logger, int notificationCount);
}
[DisallowConcurrentExecution]
public sealed partial class RoleDiscoverySweepJob(IServiceScopeFactory scopeFactory, ILogger<RoleDiscoverySweepJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleDiscovery = scope.ServiceProvider.GetRequiredService<IRoleDiscoveryService>();
        var ownerIds = await db.Users.AsNoTracking()
            .Where(user => user.EmailConfirmed)
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .Take(200)
            .ToListAsync(context.CancellationToken);

        var processed = 0;
        var proposals = 0;
        foreach (var ownerId in ownerIds)
        {
            try
            {
                var result = await roleDiscovery.DiscoverAsync(ownerId, context.CancellationToken);
                processed++;
                proposals += result.ProposalIds.Count;
            }
            catch (HttpRequestException)
            {
                LogRoleDiscoveryUnavailable(logger, ownerId);
            }
            catch (InvalidOperationException)
            {
                LogRoleDiscoveryInvalid(logger, ownerId);
            }
        }

        if (processed > 0)
        {
            LogRoleDiscoverySweepComplete(logger, processed, proposals);
        }
    }

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Role discovery sweep checked {OwnerCount} owners and retained or created {ProposalCount} pending candidates.")]
    private static partial void LogRoleDiscoverySweepComplete(ILogger logger, int ownerCount, int proposalCount);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Role discovery service was unavailable for owner {OwnerId}.")]
    private static partial void LogRoleDiscoveryUnavailable(ILogger logger, Guid ownerId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "Role discovery returned an invalid response for owner {OwnerId}.")]
    private static partial void LogRoleDiscoveryInvalid(ILogger logger, Guid ownerId);
}
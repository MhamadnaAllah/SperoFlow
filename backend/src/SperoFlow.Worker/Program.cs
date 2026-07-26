using Microsoft.Extensions.Hosting;
using Quartz;
using SperoFlow.Infrastructure;
using SperoFlow.Worker;

var builder = Host.CreateApplicationBuilder(args);

if (builder.Environment.IsProduction() || string.Equals(builder.Configuration["LOG_FORMAT"], "json", StringComparison.OrdinalIgnoreCase))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole();
}

var roleDiscoveryOptions = builder.Configuration.GetSection(RoleDiscoveryOptions.SectionName).Get<RoleDiscoveryOptions>()
    ?? new RoleDiscoveryOptions();

builder.Services.AddSperoFlowInfrastructure(builder.Configuration);
builder.Services.AddQuartz(options =>
{
    var outboxJob = new JobKey(nameof(OutboxDispatchJob));
    options.AddJob<OutboxDispatchJob>(job => job.WithIdentity(outboxJob));
    options.AddTrigger(trigger => trigger
        .ForJob(outboxJob)
        .WithIdentity("outbox-dispatch-trigger")
        .StartNow()
        .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever()));

    if (builder.Configuration.GetValue<bool>("LegacyKnowledgeIngestion:Enabled"))
    {
        var textractRecoveryJob = new JobKey(nameof(TextractOcrRecoveryJob));
        options.AddJob<TextractOcrRecoveryJob>(job => job.WithIdentity(textractRecoveryJob));
        options.AddTrigger(trigger => trigger
            .ForJob(textractRecoveryJob)
            .WithIdentity("textract-ocr-recovery-trigger")
            .StartNow()
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(30)).RepeatForever()));
    }

    var reminderJob = new JobKey(nameof(ReminderSweepJob));
    options.AddJob<ReminderSweepJob>(job => job.WithIdentity(reminderJob));
    options.AddTrigger(trigger => trigger
        .ForJob(reminderJob)
        .WithIdentity("reminder-sweep-trigger")
        .StartNow()
        .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever()));

    if (roleDiscoveryOptions.Enabled)
    {
        var roleDiscoveryJob = new JobKey(nameof(RoleDiscoverySweepJob));
        options.AddJob<RoleDiscoverySweepJob>(job => job.WithIdentity(roleDiscoveryJob));
        options.AddTrigger(trigger => trigger
            .ForJob(roleDiscoveryJob)
            .WithIdentity("role-discovery-sweep-trigger")
            .StartNow()
            .WithSimpleSchedule(schedule => schedule
                .WithInterval(TimeSpan.FromHours(Math.Clamp(roleDiscoveryOptions.SweepIntervalHours, 1, 168)))
                .RepeatForever()));
    }
});
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

var host = builder.Build();
await host.RunAsync();

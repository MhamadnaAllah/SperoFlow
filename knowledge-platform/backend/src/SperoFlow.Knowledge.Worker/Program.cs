using SperoFlow.Knowledge.Infrastructure;
using SperoFlow.Knowledge.Worker;

var builder = Host.CreateApplicationBuilder(args);

if (builder.Environment.IsProduction() || string.Equals(builder.Configuration["LOG_FORMAT"], "json", StringComparison.OrdinalIgnoreCase))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole();
}

builder.Services.AddKnowledgeInfrastructure(builder.Configuration);
builder.Services.AddHostedService<KnowledgeOutboxWorker>();

var host = builder.Build();
host.Run();
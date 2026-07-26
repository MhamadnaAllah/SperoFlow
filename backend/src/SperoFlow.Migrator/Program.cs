using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using SperoFlow.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSperoFlowInfrastructure(builder.Configuration);

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

if (!db.Database.GetMigrations().Any())
{
    throw new InvalidOperationException("No EF Core migrations are available. Generate and commit the initial migration before running db-migrate.");
}

await db.Database.MigrateAsync();

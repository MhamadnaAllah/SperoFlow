using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SperoFlow.Knowledge.Infrastructure;

public sealed class KnowledgeDbContextFactory : IDesignTimeDbContextFactory<KnowledgeDbContext>
{
    public KnowledgeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("KnowledgeDatabase__ConnectionString")
            ?? "Host=localhost;Port=5432;Database=speroflow_knowledge;Username=speroflow_knowledge;Password=change-me";
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new KnowledgeDbContext(options);
    }
}

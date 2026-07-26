using Microsoft.EntityFrameworkCore;
using SperoFlow.Knowledge.Domain;

namespace SperoFlow.Knowledge.Infrastructure;

public sealed class KnowledgeDbContext : DbContext
{
    public KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options) : base(options)
    {
    }

    public DbSet<KnowledgeDataset> Datasets => Set<KnowledgeDataset>();

    public DbSet<KnowledgeSourceFile> Sources => Set<KnowledgeSourceFile>();

    public DbSet<KnowledgeIngestionJob> IngestionJobs => Set<KnowledgeIngestionJob>();

    public DbSet<KnowledgeGraphRelease> GraphReleases => Set<KnowledgeGraphRelease>();

    public DbSet<KnowledgeOutboxMessage> OutboxMessages => Set<KnowledgeOutboxMessage>();

    public DbSet<KnowledgeAuditEvent> AuditEvents => Set<KnowledgeAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureEntity(modelBuilder.Entity<KnowledgeDataset>(), "datasets");
        modelBuilder.Entity<KnowledgeDataset>(entity =>
        {
            entity.Property(value => value.OwnerSubject).HasMaxLength(256).IsRequired();
            entity.Property(value => value.Name).HasMaxLength(240).IsRequired();
            entity.Property(value => value.Description).HasMaxLength(8_000);
            entity.Property(value => value.State).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(value => value.Visibility).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(value => new { value.OwnerSubject, value.State, value.Visibility, value.UpdatedAt });
            entity.HasIndex(value => value.PublishedReleaseId).IsUnique().HasFilter("\"PublishedReleaseId\" IS NOT NULL");
        });

        ConfigureEntity(modelBuilder.Entity<KnowledgeSourceFile>(), "sources");
        modelBuilder.Entity<KnowledgeSourceFile>(entity =>
        {
            entity.Property(value => value.OwnerSubject).HasMaxLength(256).IsRequired();
            entity.Property(value => value.FileName).HasMaxLength(500).IsRequired();
            entity.Property(value => value.ObjectKey).HasMaxLength(1_024).IsRequired();
            entity.Property(value => value.ContentType).HasMaxLength(200).IsRequired();
            entity.Property(value => value.ExpectedSha256).HasMaxLength(64).IsRequired();
            entity.Property(value => value.UploadedSha256).HasMaxLength(64);
            entity.Property(value => value.State).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(value => value.FailureReason).HasMaxLength(1_000);
            entity.HasIndex(value => new { value.DatasetId, value.State, value.CreatedAt });
            entity.HasIndex(value => value.ObjectKey).IsUnique();
            entity.HasOne<KnowledgeDataset>().WithMany().HasForeignKey(value => value.DatasetId).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureEntity(modelBuilder.Entity<KnowledgeGraphRelease>(), "graph_releases");
        modelBuilder.Entity<KnowledgeGraphRelease>(entity =>
        {
            entity.Property(value => value.OwnerSubject).HasMaxLength(256).IsRequired();
            entity.Property(value => value.ReleaseKey).HasMaxLength(200).IsRequired();
            entity.Property(value => value.State).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(value => value.ValidationReport).HasMaxLength(32_000);
            entity.HasIndex(value => new { value.DatasetId, value.State, value.CreatedAt });
            entity.HasIndex(value => value.DatasetId).IsUnique().HasFilter("\"State\" = 'Draft'");
            entity.HasIndex(value => value.ReleaseKey).IsUnique();
            entity.HasOne<KnowledgeDataset>().WithMany().HasForeignKey(value => value.DatasetId).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureEntity(modelBuilder.Entity<KnowledgeIngestionJob>(), "ingestion_jobs");
        modelBuilder.Entity<KnowledgeIngestionJob>(entity =>
        {
            entity.Property(value => value.OwnerSubject).HasMaxLength(256).IsRequired();
            entity.Property(value => value.State).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(value => value.TextractJobId).HasMaxLength(256);
            entity.Property(value => value.Report).HasMaxLength(32_000).IsRequired();
            entity.Property(value => value.FailureReason).HasMaxLength(1_000);
            entity.HasIndex(value => new { value.DatasetId, value.State, value.CreatedAt });
            entity.HasIndex(value => new { value.OwnerSubject, value.State, value.UpdatedAt });
            entity.HasIndex(value => new { value.State, value.LeaseExpiresAt });
            entity.HasIndex(value => new { value.ReleaseId, value.SourceFileId }).IsUnique();
            entity.HasOne<KnowledgeDataset>().WithMany().HasForeignKey(value => value.DatasetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<KnowledgeSourceFile>().WithMany().HasForeignKey(value => value.SourceFileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<KnowledgeGraphRelease>().WithMany().HasForeignKey(value => value.ReleaseId).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureEntity(modelBuilder.Entity<KnowledgeOutboxMessage>(), "outbox_messages");
        modelBuilder.Entity<KnowledgeOutboxMessage>(entity =>
        {
            entity.Property(value => value.OwnerSubject).HasMaxLength(256).IsRequired();
            entity.Property(value => value.Type).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Payload).HasMaxLength(64_000).IsRequired();
            entity.HasIndex(value => new { value.DispatchedAt, value.AvailableAt, value.CreatedAt });
        });

        ConfigureEntity(modelBuilder.Entity<KnowledgeAuditEvent>(), "audit_events");
        modelBuilder.Entity<KnowledgeAuditEvent>(entity =>
        {
            entity.Property(value => value.ActorSubject).HasMaxLength(256).IsRequired();
            entity.Property(value => value.Action).HasMaxLength(160).IsRequired();
            entity.Property(value => value.EntityType).HasMaxLength(160).IsRequired();
            entity.Property(value => value.Detail).HasMaxLength(4_000);
            entity.HasIndex(value => new { value.EntityType, value.EntityId, value.CreatedAt });
            entity.HasIndex(value => new { value.ActorSubject, value.CreatedAt });
        });
    }

    private static void ConfigureEntity<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity, string table)
        where TEntity : KnowledgeEntity
    {
        entity.ToTable(table, "knowledge");
        entity.HasKey(value => value.Id);
        entity.Property(value => value.ConcurrencyToken).IsConcurrencyToken();
        entity.Property(value => value.CreatedAt).IsRequired();
        entity.Property(value => value.UpdatedAt).IsRequired();
    }
}
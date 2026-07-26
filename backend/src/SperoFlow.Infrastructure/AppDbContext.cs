using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Domain;

namespace SperoFlow.Infrastructure;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<GoalMilestone> GoalMilestones => Set<GoalMilestone>();

    public DbSet<GoalRoadmapProposal> GoalRoadmapProposals => Set<GoalRoadmapProposal>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<LifeRole> LifeRoles => Set<LifeRole>();

    public DbSet<RoleDiscoveryFinding> RoleDiscoveryFindings => Set<RoleDiscoveryFinding>();

    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();

    public DbSet<Habit> Habits => Set<Habit>();

    public DbSet<HabitCheckIn> HabitCheckIns => Set<HabitCheckIn>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<JournalInsight> JournalInsights => Set<JournalInsight>();

    public DbSet<DocumentAsset> Documents => Set<DocumentAsset>();

    public DbSet<IngestionJob> IngestionJobs => Set<IngestionJob>();

    public DbSet<InAppNotification> Notifications => Set<InAppNotification>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<BalanceProposal> BalanceProposals => Set<BalanceProposal>();

    public DbSet<AiActionProposal> AiActionProposals => Set<AiActionProposal>();

    public DbSet<KnowledgeDataset> KnowledgeDatasets => Set<KnowledgeDataset>();

    public DbSet<KnowledgeSourceFile> KnowledgeSourceFiles => Set<KnowledgeSourceFile>();

    public DbSet<DatasetIngestionJob> DatasetIngestionJobs => Set<DatasetIngestionJob>();

    public DbSet<CoachConversation> CoachConversations => Set<CoachConversation>();

    public DbSet<CoachMessage> CoachMessages => Set<CoachMessage>();

    public DbSet<CoachObservation> CoachObservations => Set<CoachObservation>();

    public DbSet<AdminBootstrap> AdminBootstraps => Set<AdminBootstrap>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>().Where(entry => entry.State == EntityState.Modified))
        {
            entry.Entity.Touch();
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("app");
        base.OnModelCreating(builder);
        builder.UseOpenIddict();

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(user => user.DisplayName).HasMaxLength(160);
        });
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        ConfigureAuditable(builder.Entity<Project>(), "projects");
        builder.Entity<Project>(entity =>
        {
            entity.Property(project => project.Name).HasMaxLength(240).IsRequired();
            entity.Property(project => project.Description).HasMaxLength(8_000);
            entity.Property(project => project.Color).HasMaxLength(40).IsRequired();
            entity.Property(project => project.Icon).HasMaxLength(64).IsRequired();
            entity.Property(project => project.State).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(project => new { project.OwnerId, project.State, project.SortOrder });
        });
        ConfigureAuditable(builder.Entity<Goal>(), "goals");
        builder.Entity<Goal>(entity =>
        {
            entity.Property(goal => goal.Title).HasMaxLength(240).IsRequired();
            entity.Property(goal => goal.Description).HasMaxLength(8_000);
            entity.Property(goal => goal.LifeArea).HasConversion<string>().HasMaxLength(32);
            entity.Property(goal => goal.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(goal => goal.RoadmapSummary).HasMaxLength(4_000);
            entity.HasIndex(goal => new { goal.OwnerId, goal.State, goal.SortOrder });
            entity.HasIndex(goal => new { goal.OwnerId, goal.RoleId });
            entity.HasOne<LifeRole>()
                .WithMany()
                .HasForeignKey(goal => goal.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureAuditable(builder.Entity<GoalMilestone>(), "goal_milestones");
        builder.Entity<GoalMilestone>(entity =>
        {
            entity.Property(milestone => milestone.Title).HasMaxLength(300).IsRequired();
            entity.Property(milestone => milestone.Description).HasMaxLength(4_000);
            entity.Property(milestone => milestone.EstimatedHours).HasPrecision(8, 2);
            entity.Property(milestone => milestone.State).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(milestone => new { milestone.OwnerId, milestone.GoalId, milestone.State, milestone.SortOrder });
            entity.HasOne<Goal>()
                .WithMany()
                .HasForeignKey(milestone => milestone.GoalId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureAuditable(builder.Entity<GoalRoadmapProposal>(), "goal_roadmap_proposals");
        builder.Entity<GoalRoadmapProposal>(entity =>
        {
            entity.Property(proposal => proposal.ProtectedPayload).HasMaxLength(48_000).IsRequired();
            entity.Property(proposal => proposal.State).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(proposal => proposal.ProposalId).IsUnique();
            entity.HasIndex(proposal => new { proposal.OwnerId, proposal.GoalId, proposal.State, proposal.CreatedAt });
            entity.HasOne<AiActionProposal>()
                .WithMany()
                .HasForeignKey(proposal => proposal.ProposalId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Goal>()
                .WithMany()
                .HasForeignKey(proposal => proposal.GoalId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureAuditable(builder.Entity<LifeRole>(), "life_roles");
        builder.Entity<LifeRole>(entity =>
        {
            entity.Property(role => role.Name).HasMaxLength(160).IsRequired();
            entity.Property(role => role.Category).HasConversion<string>().HasMaxLength(32);
            entity.Property(role => role.DefaultLifeArea).HasConversion<string>().HasMaxLength(32);
            entity.Property(role => role.Color).HasMaxLength(40).IsRequired();
            entity.Property(role => role.Icon).HasMaxLength(64).IsRequired();
            entity.Property(role => role.SystemKey).HasMaxLength(32);
            entity.HasIndex(role => new { role.OwnerId, role.IsArchived, role.SortOrder });
            entity.HasIndex(role => new { role.OwnerId, role.SystemKey }).IsUnique();
        });

        ConfigureAuditable(builder.Entity<RoleDiscoveryFinding>(), "role_discovery_findings");
        builder.Entity<RoleDiscoveryFinding>(entity =>
        {
            entity.Property(finding => finding.ProtectedEvidence).HasMaxLength(24_000).IsRequired();
            entity.Property(finding => finding.State).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(finding => finding.ProposalId).IsUnique();
            entity.HasIndex(finding => new { finding.OwnerId, finding.State, finding.CreatedAt });
            entity.HasOne<AiActionProposal>()
                .WithMany()
                .HasForeignKey(finding => finding.ProposalId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureAuditable(builder.Entity<TaskItem>(), "tasks");
        builder.Entity<TaskItem>(entity =>
        {
            entity.Property(task => task.Title).HasMaxLength(500).IsRequired();
            entity.Property(task => task.Description).HasMaxLength(8_000);
            entity.Property(task => task.LifeArea).HasConversion<string>().HasMaxLength(32);
            entity.Property(task => task.Quadrant).HasConversion<string>().HasMaxLength(16);
            entity.Property(task => task.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(task => task.ProjectId);
            entity.Property(task => task.RoleId);
            entity.Property(task => task.GoalId);
            entity.Property(task => task.Source).HasMaxLength(100);
            entity.HasIndex(task => new { task.OwnerId, task.State, task.DueAt });
            entity.HasIndex(task => new { task.OwnerId, task.ProjectId, task.State, task.SortOrder });
            entity.HasIndex(task => new { task.OwnerId, task.GoalId, task.State, task.SortOrder });
            entity.HasOne<Project>()
                .WithMany()
                .HasForeignKey(task => task.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LifeRole>()
                .WithMany()
                .HasForeignKey(task => task.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Goal>()
                .WithMany()
                .HasForeignKey(task => task.GoalId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureAuditable(builder.Entity<CalendarEvent>(), "calendar_events");
        builder.Entity<CalendarEvent>(entity =>
        {
            entity.Property(calendarEvent => calendarEvent.Title).HasMaxLength(500).IsRequired();
            entity.Property(calendarEvent => calendarEvent.Color).HasMaxLength(40).IsRequired();
            entity.Property(calendarEvent => calendarEvent.Role).HasMaxLength(100);
            entity.HasIndex(calendarEvent => new { calendarEvent.OwnerId, calendarEvent.StartsAt });
        });

        ConfigureAuditable(builder.Entity<Habit>(), "habits");
        builder.Entity<Habit>(entity =>
        {
            entity.Property(habit => habit.Title).HasMaxLength(300).IsRequired();
            entity.Property(habit => habit.Description).HasMaxLength(4_000);
            entity.Property(habit => habit.LifeArea).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(habit => new { habit.OwnerId, habit.IsArchived });
        });

        ConfigureAuditable(builder.Entity<HabitCheckIn>(), "habit_check_ins");
        builder.Entity<HabitCheckIn>(entity =>
        {
            entity.Property(checkIn => checkIn.Note).HasMaxLength(2_000);
            entity.HasIndex(checkIn => new { checkIn.OwnerId, checkIn.HabitId, checkIn.OccurredOn }).IsUnique();
        });

        ConfigureAuditable(builder.Entity<JournalEntry>(), "journal_entries");
        builder.Entity<JournalEntry>(entity =>
        {
            entity.Property(entry => entry.ProtectedContent).IsRequired();
            entity.Property(entry => entry.Mood).HasMaxLength(32);
            entity.HasIndex(entry => new { entry.OwnerId, entry.CreatedAt });
        });

        ConfigureAuditable(builder.Entity<JournalInsight>(), "journal_insights");
        builder.Entity<JournalInsight>(entity =>
        {
            entity.Property(insight => insight.ProtectedPayload).HasMaxLength(24_000).IsRequired();
            entity.Property(insight => insight.State).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(insight => new { insight.OwnerId, insight.JournalEntryId, insight.SourceConcurrencyToken }).IsUnique();
            entity.HasIndex(insight => new { insight.OwnerId, insight.JournalEntryId, insight.State, insight.CreatedAt });
            entity.HasOne<JournalEntry>()
                .WithMany()
                .HasForeignKey(insight => insight.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureAuditable(builder.Entity<DocumentAsset>(), "documents");
        builder.Entity<DocumentAsset>(entity =>
        {
            entity.Property(document => document.Title).HasMaxLength(500).IsRequired();
            entity.Property(document => document.ObjectKey).HasMaxLength(1_000).IsRequired();
            entity.Property(document => document.ContentType).HasMaxLength(255).IsRequired();
            entity.Property(document => document.State).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(document => new { document.OwnerId, document.CreatedAt });
        });

        ConfigureAuditable(builder.Entity<IngestionJob>(), "ingestion_jobs");
        builder.Entity<IngestionJob>(entity =>
        {
            entity.Property(job => job.RoadmapName).HasMaxLength(300).IsRequired();
            entity.Property(job => job.SourceType).HasMaxLength(40).IsRequired();
            entity.Property(job => job.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(job => job.FailureReason).HasMaxLength(1_000);
            entity.HasIndex(job => new { job.OwnerId, job.DocumentId }).IsUnique();
        });

        ConfigureAuditable(builder.Entity<InAppNotification>(), "notifications");
        builder.Entity<InAppNotification>(entity =>
        {
            entity.Property(notification => notification.Category).HasMaxLength(80).IsRequired();
            entity.Property(notification => notification.Title).HasMaxLength(300).IsRequired();
            entity.Property(notification => notification.Body).HasMaxLength(4_000).IsRequired();
            entity.Property(notification => notification.SourceKey).HasMaxLength(300).IsRequired();
            entity.HasIndex(notification => new { notification.OwnerId, notification.SourceKey }).IsUnique();
        });

        builder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Category).HasMaxLength(100).IsRequired();
            entity.Property(audit => audit.Action).HasMaxLength(100).IsRequired();
            entity.Property(audit => audit.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(audit => audit.Metadata).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(audit => new { audit.OwnerId, audit.OccurredAt });
        });

        builder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Type).HasMaxLength(200).IsRequired();
            entity.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(message => new { message.DispatchedAt, message.CreatedAt });
        });

        ConfigureAuditable(builder.Entity<AiActionProposal>(), "ai_action_proposals");
        builder.Entity<AiActionProposal>(entity =>
        {
            entity.Property(proposal => proposal.Kind).HasConversion<string>().HasMaxLength(32);
            entity.Property(proposal => proposal.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(proposal => proposal.Source).HasMaxLength(100).IsRequired();
            entity.Property(proposal => proposal.SourceKey).HasMaxLength(160).IsRequired();
            entity.Property(proposal => proposal.Title).HasMaxLength(300).IsRequired();
            entity.Property(proposal => proposal.Description).HasMaxLength(4_000).IsRequired();
            entity.Property(proposal => proposal.Payload).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(proposal => new { proposal.OwnerId, proposal.SourceKey }).IsUnique();
            entity.HasIndex(proposal => new { proposal.OwnerId, proposal.State, proposal.CreatedAt });
        });

        ConfigureAuditable(builder.Entity<KnowledgeDataset>(), "knowledge_datasets");
        builder.Entity<KnowledgeDataset>(entity =>
        {
            entity.Property(dataset => dataset.Name).HasMaxLength(240).IsRequired();
            entity.Property(dataset => dataset.Description).HasMaxLength(8_000);
            entity.Property(dataset => dataset.State).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(dataset => new { dataset.OwnerId, dataset.State, dataset.CreatedAt });
        });

        ConfigureAuditable(builder.Entity<KnowledgeSourceFile>(), "knowledge_source_files");
        builder.Entity<KnowledgeSourceFile>(entity =>
        {
            entity.Property(source => source.FileName).HasMaxLength(500).IsRequired();
            entity.Property(source => source.ObjectKey).HasMaxLength(1_000).IsRequired();
            entity.Property(source => source.ContentType).HasMaxLength(255).IsRequired();
            entity.Property(source => source.ExpectedSha256).HasMaxLength(64).IsRequired();
            entity.Property(source => source.UploadedSha256).HasMaxLength(64);
            entity.Property(source => source.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(source => source.FailureReason).HasMaxLength(1_000);
            entity.HasIndex(source => source.ObjectKey).IsUnique();
            entity.HasIndex(source => new { source.DatasetId, source.OwnerId, source.State });
            entity.HasOne<KnowledgeDataset>()
                .WithMany()
                .HasForeignKey(source => source.DatasetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureAuditable(builder.Entity<DatasetIngestionJob>(), "dataset_ingestion_jobs");
        builder.Entity<DatasetIngestionJob>(entity =>
        {
            entity.Property(job => job.State).HasConversion<string>().HasMaxLength(40);
            entity.Property(job => job.TextractJobId).HasMaxLength(200);
            entity.Property(job => job.Report).HasColumnType("jsonb").IsRequired();
            entity.Property(job => job.FailureReason).HasMaxLength(1_000);
            entity.HasIndex(job => job.SourceFileId).IsUnique();
            entity.HasIndex(job => new { job.OwnerId, job.DatasetId, job.State, job.CreatedAt });
            entity.HasOne<KnowledgeDataset>()
                .WithMany()
                .HasForeignKey(job => job.DatasetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<KnowledgeSourceFile>()
                .WithMany()
                .HasForeignKey(job => job.SourceFileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AdminBootstrap>(entity =>
        {
            entity.ToTable("admin_bootstrap");
            entity.HasKey(bootstrap => bootstrap.Id);
            entity.Property(bootstrap => bootstrap.Id).ValueGeneratedNever();
            entity.HasIndex(bootstrap => bootstrap.UserId).IsUnique();
        });
        ConfigureAuditable(builder.Entity<BalanceProposal>(), "balance_proposals");
        builder.Entity<BalanceProposal>(entity =>
        {
            entity.Property(proposal => proposal.AuditKey).HasMaxLength(128).IsRequired();
            entity.Property(proposal => proposal.RiskLevel).HasConversion<string>().HasMaxLength(32);
            entity.Property(proposal => proposal.Insight).HasMaxLength(600).IsRequired();
            entity.Property(proposal => proposal.SuggestedTitle).HasMaxLength(200);
            entity.Property(proposal => proposal.SuggestedDescription).HasMaxLength(500);
            entity.HasIndex(proposal => new { proposal.OwnerId, proposal.AuditKey }).IsUnique();
        });

        ConfigureAuditable(builder.Entity<CoachConversation>(), "coach_conversations");
        builder.Entity<CoachConversation>(entity =>
        {
            entity.Property(conversation => conversation.Title).HasMaxLength(300).IsRequired();
            entity.HasIndex(conversation => new { conversation.OwnerId, conversation.IsArchived, conversation.CreatedAt });
        });

        ConfigureAuditable(builder.Entity<CoachMessage>(), "coach_messages");
        builder.Entity<CoachMessage>(entity =>
        {
            entity.Property(message => message.SenderRole).HasConversion<string>().HasMaxLength(32);
            entity.Property(message => message.ProtectedContent).HasMaxLength(16_000).IsRequired();
            entity.HasIndex(message => new { message.ConversationId, message.OwnerId, message.CreatedAt });
            entity.HasOne<CoachConversation>()
                .WithMany()
                .HasForeignKey(message => message.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureAuditable(builder.Entity<CoachObservation>(), "coach_observations");
        builder.Entity<CoachObservation>(entity =>
        {
            entity.Property(observation => observation.Scope).HasConversion<string>().HasMaxLength(40);
            entity.Property(observation => observation.ProtectedContent).HasMaxLength(16_000).IsRequired();
            entity.HasIndex(observation => new { observation.OwnerId, observation.IsDismissed, observation.CreatedAt });
        });
    }

    private static void ConfigureAuditable<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity, string tableName)
        where TEntity : AuditableEntity
    {
        entity.ToTable(tableName);
        entity.HasKey(value => value.Id);
        entity.Property(value => value.ConcurrencyToken).IsConcurrencyToken();
        entity.HasIndex(value => value.OwnerId);
    }
}

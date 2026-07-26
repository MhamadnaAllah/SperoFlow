using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SperoFlow.Knowledge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "knowledge");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Detail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "datasets",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PublishedReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_datasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "graph_releases",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ReleaseKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ValidationReport = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_graph_releases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_graph_releases_datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalSchema: "knowledge",
                        principalTable: "datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sources",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpectedSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UploadedSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    UploadedSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sources_datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalSchema: "knowledge",
                        principalTable: "datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_jobs",
                schema: "knowledge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    TextractJobId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Report = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ingestion_jobs_datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalSchema: "knowledge",
                        principalTable: "datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ingestion_jobs_graph_releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalSchema: "knowledge",
                        principalTable: "graph_releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ingestion_jobs_sources_SourceFileId",
                        column: x => x.SourceFileId,
                        principalSchema: "knowledge",
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_ActorSubject_CreatedAt",
                schema: "knowledge",
                table: "audit_events",
                columns: new[] { "ActorSubject", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_EntityType_EntityId_CreatedAt",
                schema: "knowledge",
                table: "audit_events",
                columns: new[] { "EntityType", "EntityId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_datasets_OwnerSubject_State_Visibility_UpdatedAt",
                schema: "knowledge",
                table: "datasets",
                columns: new[] { "OwnerSubject", "State", "Visibility", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_datasets_PublishedReleaseId",
                schema: "knowledge",
                table: "datasets",
                column: "PublishedReleaseId",
                unique: true,
                filter: "\"PublishedReleaseId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_graph_releases_DatasetId_State_CreatedAt",
                schema: "knowledge",
                table: "graph_releases",
                columns: new[] { "DatasetId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_graph_releases_ReleaseKey",
                schema: "knowledge",
                table: "graph_releases",
                column: "ReleaseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_DatasetId_State_CreatedAt",
                schema: "knowledge",
                table: "ingestion_jobs",
                columns: new[] { "DatasetId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_OwnerSubject_State_UpdatedAt",
                schema: "knowledge",
                table: "ingestion_jobs",
                columns: new[] { "OwnerSubject", "State", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_ReleaseId",
                schema: "knowledge",
                table: "ingestion_jobs",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_SourceFileId",
                schema: "knowledge",
                table: "ingestion_jobs",
                column: "SourceFileId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_DispatchedAt_CreatedAt",
                schema: "knowledge",
                table: "outbox_messages",
                columns: new[] { "DispatchedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sources_DatasetId_State_CreatedAt",
                schema: "knowledge",
                table: "sources",
                columns: new[] { "DatasetId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sources_ObjectKey",
                schema: "knowledge",
                table: "sources",
                column: "ObjectKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "ingestion_jobs",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "graph_releases",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "sources",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "datasets",
                schema: "knowledge");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SperoFlow.Infrastructure.Migrations;

public partial class AddKnowledgeDatasetIngestion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "admin_bootstrap",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_admin_bootstrap", value => value.Id);
            });

        migrationBuilder.CreateTable(
            name: "knowledge_datasets",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_knowledge_datasets", value => value.Id);
            });

        migrationBuilder.CreateTable(
            name: "knowledge_source_files",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ObjectKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ContentType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ExpectedSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ExpectedSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UploadedSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                UploadedSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_knowledge_source_files", value => value.Id);
                table.ForeignKey(
                    name: "FK_knowledge_source_files_knowledge_datasets_DatasetId",
                    column: value => value.DatasetId,
                    principalSchema: "app",
                    principalTable: "knowledge_datasets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "dataset_ingestion_jobs",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceFileId = table.Column<Guid>(type: "uuid", nullable: false),
                State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                TextractJobId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Report = table.Column<string>(type: "jsonb", nullable: false),
                FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dataset_ingestion_jobs", value => value.Id);
                table.ForeignKey(
                    name: "FK_dataset_ingestion_jobs_knowledge_datasets_DatasetId",
                    column: value => value.DatasetId,
                    principalSchema: "app",
                    principalTable: "knowledge_datasets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_dataset_ingestion_jobs_knowledge_source_files_SourceFileId",
                    column: value => value.SourceFileId,
                    principalSchema: "app",
                    principalTable: "knowledge_source_files",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_admin_bootstrap_UserId",
            schema: "app",
            table: "admin_bootstrap",
            column: "UserId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_knowledge_datasets_OwnerId",
            schema: "app",
            table: "knowledge_datasets",
            column: "OwnerId");
        migrationBuilder.CreateIndex(
            name: "IX_knowledge_datasets_OwnerId_State_CreatedAt",
            schema: "app",
            table: "knowledge_datasets",
            columns: new[] { "OwnerId", "State", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_knowledge_source_files_DatasetId",
            schema: "app",
            table: "knowledge_source_files",
            column: "DatasetId");
        migrationBuilder.CreateIndex(
            name: "IX_knowledge_source_files_ObjectKey",
            schema: "app",
            table: "knowledge_source_files",
            column: "ObjectKey",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_knowledge_source_files_OwnerId",
            schema: "app",
            table: "knowledge_source_files",
            column: "OwnerId");
        migrationBuilder.CreateIndex(
            name: "IX_knowledge_source_files_DatasetId_OwnerId_State",
            schema: "app",
            table: "knowledge_source_files",
            columns: new[] { "DatasetId", "OwnerId", "State" });
        migrationBuilder.CreateIndex(
            name: "IX_dataset_ingestion_jobs_DatasetId",
            schema: "app",
            table: "dataset_ingestion_jobs",
            column: "DatasetId");
        migrationBuilder.CreateIndex(
            name: "IX_dataset_ingestion_jobs_SourceFileId",
            schema: "app",
            table: "dataset_ingestion_jobs",
            column: "SourceFileId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_dataset_ingestion_jobs_OwnerId",
            schema: "app",
            table: "dataset_ingestion_jobs",
            column: "OwnerId");
        migrationBuilder.CreateIndex(
            name: "IX_dataset_ingestion_jobs_OwnerId_DatasetId_State_CreatedAt",
            schema: "app",
            table: "dataset_ingestion_jobs",
            columns: new[] { "OwnerId", "DatasetId", "State", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "dataset_ingestion_jobs", schema: "app");
        migrationBuilder.DropTable(name: "admin_bootstrap", schema: "app");
        migrationBuilder.DropTable(name: "knowledge_source_files", schema: "app");
        migrationBuilder.DropTable(name: "knowledge_datasets", schema: "app");
    }
}
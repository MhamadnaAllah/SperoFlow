using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SperoFlow.Knowledge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseSnapshotInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ingestion_jobs_ReleaseId",
                schema: "knowledge",
                table: "ingestion_jobs");

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_ReleaseId_SourceFileId",
                schema: "knowledge",
                table: "ingestion_jobs",
                columns: new[] { "ReleaseId", "SourceFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_graph_releases_DatasetId",
                schema: "knowledge",
                table: "graph_releases",
                column: "DatasetId",
                unique: true,
                filter: "\"State\" = 'Draft'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ingestion_jobs_ReleaseId_SourceFileId",
                schema: "knowledge",
                table: "ingestion_jobs");

            migrationBuilder.DropIndex(
                name: "IX_graph_releases_DatasetId",
                schema: "knowledge",
                table: "graph_releases");

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_jobs_ReleaseId",
                schema: "knowledge",
                table: "ingestion_jobs",
                column: "ReleaseId");
        }
    }
}

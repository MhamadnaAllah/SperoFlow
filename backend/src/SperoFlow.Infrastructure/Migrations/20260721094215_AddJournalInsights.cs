using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SperoFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "journal_insights",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedPayload = table.Column<string>(type: "character varying(24000)", maxLength: 24000, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journal_insights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_journal_insights_journal_entries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalSchema: "app",
                        principalTable: "journal_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_journal_insights_JournalEntryId",
                schema: "app",
                table: "journal_insights",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_journal_insights_OwnerId",
                schema: "app",
                table: "journal_insights",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_journal_insights_OwnerId_JournalEntryId_SourceConcurrencyTo~",
                schema: "app",
                table: "journal_insights",
                columns: new[] { "OwnerId", "JournalEntryId", "SourceConcurrencyToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_insights_OwnerId_JournalEntryId_State_CreatedAt",
                schema: "app",
                table: "journal_insights",
                columns: new[] { "OwnerId", "JournalEntryId", "State", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journal_insights",
                schema: "app");
        }
    }
}

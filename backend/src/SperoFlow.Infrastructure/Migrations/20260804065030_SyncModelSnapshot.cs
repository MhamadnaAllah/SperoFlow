using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SperoFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coach_conversations",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coach_conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "coach_observations",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProtectedContent = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDismissed = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coach_observations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "coach_messages",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProtectedContent = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coach_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_coach_messages_coach_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "app",
                        principalTable: "coach_conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_coach_conversations_OwnerId",
                schema: "app",
                table: "coach_conversations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_coach_conversations_OwnerId_IsArchived_CreatedAt",
                schema: "app",
                table: "coach_conversations",
                columns: new[] { "OwnerId", "IsArchived", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_coach_messages_ConversationId_OwnerId_CreatedAt",
                schema: "app",
                table: "coach_messages",
                columns: new[] { "ConversationId", "OwnerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_coach_messages_OwnerId",
                schema: "app",
                table: "coach_messages",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_coach_observations_OwnerId",
                schema: "app",
                table: "coach_observations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_coach_observations_OwnerId_IsDismissed_CreatedAt",
                schema: "app",
                table: "coach_observations",
                columns: new[] { "OwnerId", "IsDismissed", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coach_messages",
                schema: "app");

            migrationBuilder.DropTable(
                name: "coach_observations",
                schema: "app");

            migrationBuilder.DropTable(
                name: "coach_conversations",
                schema: "app");
        }
    }
}

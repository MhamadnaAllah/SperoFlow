using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SperoFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLifeRolesAndAiActionProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RoleId",
                schema: "app",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ai_action_proposals",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    AppliedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_action_proposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "life_roles",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DefaultLifeArea = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Color = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    SystemKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_life_roles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_RoleId",
                schema: "app",
                table: "tasks",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_action_proposals_OwnerId",
                schema: "app",
                table: "ai_action_proposals",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_action_proposals_OwnerId_SourceKey",
                schema: "app",
                table: "ai_action_proposals",
                columns: new[] { "OwnerId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_action_proposals_OwnerId_State_CreatedAt",
                schema: "app",
                table: "ai_action_proposals",
                columns: new[] { "OwnerId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_life_roles_OwnerId",
                schema: "app",
                table: "life_roles",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_life_roles_OwnerId_IsArchived_SortOrder",
                schema: "app",
                table: "life_roles",
                columns: new[] { "OwnerId", "IsArchived", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_life_roles_OwnerId_SystemKey",
                schema: "app",
                table: "life_roles",
                columns: new[] { "OwnerId", "SystemKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_life_roles_RoleId",
                schema: "app",
                table: "tasks",
                column: "RoleId",
                principalSchema: "app",
                principalTable: "life_roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_life_roles_RoleId",
                schema: "app",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "ai_action_proposals",
                schema: "app");

            migrationBuilder.DropTable(
                name: "life_roles",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "IX_tasks_RoleId",
                schema: "app",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "RoleId",
                schema: "app",
                table: "tasks");
        }
    }
}

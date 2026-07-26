using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SperoFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalsRoadmapsAndEisenhowerProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GoalId",
                schema: "app",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "goals",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    LifeArea = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    RoadmapSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_goals_life_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "app",
                        principalTable: "life_roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goal_milestones",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EstimatedHours = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goal_milestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_goal_milestones_goals_GoalId",
                        column: x => x.GoalId,
                        principalSchema: "app",
                        principalTable: "goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goal_roadmap_proposals",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedPayload = table.Column<string>(type: "character varying(48000)", maxLength: 48000, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_goal_roadmap_proposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_goal_roadmap_proposals_ai_action_proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "app",
                        principalTable: "ai_action_proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_goal_roadmap_proposals_goals_GoalId",
                        column: x => x.GoalId,
                        principalSchema: "app",
                        principalTable: "goals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_GoalId",
                schema: "app",
                table: "tasks",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_tasks_OwnerId_GoalId_State_SortOrder",
                schema: "app",
                table: "tasks",
                columns: new[] { "OwnerId", "GoalId", "State", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_goal_milestones_GoalId",
                schema: "app",
                table: "goal_milestones",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_goal_milestones_OwnerId",
                schema: "app",
                table: "goal_milestones",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_goal_milestones_OwnerId_GoalId_State_SortOrder",
                schema: "app",
                table: "goal_milestones",
                columns: new[] { "OwnerId", "GoalId", "State", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_goal_roadmap_proposals_GoalId",
                schema: "app",
                table: "goal_roadmap_proposals",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_goal_roadmap_proposals_OwnerId",
                schema: "app",
                table: "goal_roadmap_proposals",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_goal_roadmap_proposals_OwnerId_GoalId_State_CreatedAt",
                schema: "app",
                table: "goal_roadmap_proposals",
                columns: new[] { "OwnerId", "GoalId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_goal_roadmap_proposals_ProposalId",
                schema: "app",
                table: "goal_roadmap_proposals",
                column: "ProposalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_goals_OwnerId",
                schema: "app",
                table: "goals",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_goals_OwnerId_RoleId",
                schema: "app",
                table: "goals",
                columns: new[] { "OwnerId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_goals_OwnerId_State_SortOrder",
                schema: "app",
                table: "goals",
                columns: new[] { "OwnerId", "State", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_goals_RoleId",
                schema: "app",
                table: "goals",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_goals_GoalId",
                schema: "app",
                table: "tasks",
                column: "GoalId",
                principalSchema: "app",
                principalTable: "goals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_goals_GoalId",
                schema: "app",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "goal_milestones",
                schema: "app");

            migrationBuilder.DropTable(
                name: "goal_roadmap_proposals",
                schema: "app");

            migrationBuilder.DropTable(
                name: "goals",
                schema: "app");

            migrationBuilder.DropIndex(
                name: "IX_tasks_GoalId",
                schema: "app",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_OwnerId_GoalId_State_SortOrder",
                schema: "app",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "GoalId",
                schema: "app",
                table: "tasks");
        }
    }
}

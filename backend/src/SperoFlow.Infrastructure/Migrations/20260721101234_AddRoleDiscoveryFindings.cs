using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SperoFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleDiscoveryFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role_discovery_findings",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedEvidence = table.Column<string>(type: "character varying(24000)", maxLength: 24000, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_discovery_findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_discovery_findings_ai_action_proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "app",
                        principalTable: "ai_action_proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_role_discovery_findings_OwnerId",
                schema: "app",
                table: "role_discovery_findings",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_role_discovery_findings_OwnerId_State_CreatedAt",
                schema: "app",
                table: "role_discovery_findings",
                columns: new[] { "OwnerId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_role_discovery_findings_ProposalId",
                schema: "app",
                table: "role_discovery_findings",
                column: "ProposalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_discovery_findings",
                schema: "app");
        }
    }
}

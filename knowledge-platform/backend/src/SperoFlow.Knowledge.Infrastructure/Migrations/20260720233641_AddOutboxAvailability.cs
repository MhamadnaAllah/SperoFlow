using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SperoFlow.Knowledge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_DispatchedAt_CreatedAt",
                schema: "knowledge",
                table: "outbox_messages");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AvailableAt",
                schema: "knowledge",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_DispatchedAt_AvailableAt_CreatedAt",
                schema: "knowledge",
                table: "outbox_messages",
                columns: new[] { "DispatchedAt", "AvailableAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_DispatchedAt_AvailableAt_CreatedAt",
                schema: "knowledge",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "AvailableAt",
                schema: "knowledge",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_DispatchedAt_CreatedAt",
                schema: "knowledge",
                table: "outbox_messages",
                columns: new[] { "DispatchedAt", "CreatedAt" });
        }
    }
}

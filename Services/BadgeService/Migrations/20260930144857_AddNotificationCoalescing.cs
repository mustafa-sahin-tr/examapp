using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationCoalescing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CoalescedCount",
                table: "Notifications",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "LatestCommentId",
                table: "Notifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RootCommentId",
                table: "Notifications",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationEventLogs",
                columns: table => new
                {
                    Type = table.Column<string>(type: "text", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationEventLogs", x => new { x.Type, x.EventId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_LatestCommentId",
                table: "Notifications",
                column: "LatestCommentId",
                filter: "\"LatestCommentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Type_RootCommentId",
                table: "Notifications",
                columns: new[] { "UserId", "Type", "RootCommentId" },
                unique: true,
                filter: "\"RootCommentId\" IS NOT NULL AND \"IsRead\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEventLogs_NotificationId",
                table: "NotificationEventLogs",
                column: "NotificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationEventLogs");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_LatestCommentId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_Type_RootCommentId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "CoalescedCount",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "LatestCommentId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "RootCommentId",
                table: "Notifications");
        }
    }
}

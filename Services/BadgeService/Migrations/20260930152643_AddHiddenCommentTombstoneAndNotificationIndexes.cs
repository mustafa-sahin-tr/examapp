using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddHiddenCommentTombstoneAndNotificationIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_Type_RootCommentId",
                table: "Notifications");

            migrationBuilder.CreateTable(
                name: "HiddenCommentTombstones",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    HiddenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiddenCommentTombstones", x => x.CommentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_UserKeycloakId_Type_RootCommentId",
                table: "Notifications",
                columns: new[] { "UserId", "UserKeycloakId", "Type", "RootCommentId" },
                unique: true,
                filter: "\"RootCommentId\" IS NOT NULL AND \"IsRead\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationEventLogs_ProcessedAt",
                table: "NotificationEventLogs",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HiddenCommentTombstones");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_UserKeycloakId_Type_RootCommentId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_NotificationEventLogs_ProcessedAt",
                table: "NotificationEventLogs");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Type_RootCommentId",
                table: "Notifications",
                columns: new[] { "UserId", "Type", "RootCommentId" },
                unique: true,
                filter: "\"RootCommentId\" IS NOT NULL AND \"IsRead\" = FALSE");
        }
    }
}

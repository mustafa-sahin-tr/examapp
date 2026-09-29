using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSourceBadgeDefinitionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceBadgeDefinitionId",
                table: "Notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_SourceBadgeDefinitionId",
                table: "Notifications",
                columns: new[] { "UserId", "SourceBadgeDefinitionId" },
                unique: true,
                filter: "\"SourceBadgeDefinitionId\" IS NOT NULL AND \"Type\" = 'BadgeEarned'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_SourceBadgeDefinitionId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "SourceBadgeDefinitionId",
                table: "Notifications");
        }
    }
}

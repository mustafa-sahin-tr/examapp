using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationUserKeycloakId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserKeycloakId",
                table: "Notifications",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserKeycloakId_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserKeycloakId", "IsRead", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserKeycloakId_IsRead_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "UserKeycloakId",
                table: "Notifications");
        }
    }
}

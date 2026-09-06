using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSourceAccessRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceAccessRequestId",
                table: "Notifications",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type_SourceAccessRequestId",
                table: "Notifications",
                columns: new[] { "Type", "SourceAccessRequestId" },
                unique: true,
                filter: "\"SourceAccessRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_Type_SourceAccessRequestId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "SourceAccessRequestId",
                table: "Notifications");
        }
    }
}

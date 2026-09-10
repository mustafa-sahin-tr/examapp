using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSourceTeacherApplicationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceTeacherApplicationId",
                table: "Notifications",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type_SourceTeacherApplicationId",
                table: "Notifications",
                columns: new[] { "Type", "SourceTeacherApplicationId" },
                unique: true,
                filter: "\"SourceTeacherApplicationId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_Type_SourceTeacherApplicationId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "SourceTeacherApplicationId",
                table: "Notifications");
        }
    }
}

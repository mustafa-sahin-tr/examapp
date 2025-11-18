using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddBadgePaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PathKey",
                table: "BadgeDefinitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PathName",
                table: "BadgeDefinitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PathOrder",
                table: "BadgeDefinitions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PathKey",
                table: "BadgeDefinitions");

            migrationBuilder.DropColumn(
                name: "PathName",
                table: "BadgeDefinitions");

            migrationBuilder.DropColumn(
                name: "PathOrder",
                table: "BadgeDefinitions");
        }
    }
}

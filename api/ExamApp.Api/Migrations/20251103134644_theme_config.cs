using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class theme_config : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThemeCustomConfig",
                table: "Teachers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemePreset",
                table: "Teachers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeCustomConfig",
                table: "Students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemePreset",
                table: "Students",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThemeCustomConfig",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "ThemePreset",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "ThemeCustomConfig",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ThemePreset",
                table: "Students");
        }
    }
}

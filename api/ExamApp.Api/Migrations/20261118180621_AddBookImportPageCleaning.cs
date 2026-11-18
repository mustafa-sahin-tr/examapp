using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookImportPageCleaning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CleanImageUrl",
                table: "BookImportPages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarksJson",
                table: "BookImportPages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalImageUrl",
                table: "BookImportPages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CleanJson",
                table: "BookImportJobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CleanImageUrl",
                table: "BookImportPages");

            migrationBuilder.DropColumn(
                name: "MarksJson",
                table: "BookImportPages");

            migrationBuilder.DropColumn(
                name: "OriginalImageUrl",
                table: "BookImportPages");

            migrationBuilder.DropColumn(
                name: "CleanJson",
                table: "BookImportJobs");
        }
    }
}

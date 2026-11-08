using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class BookImportQuestionDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "Worksheets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "Questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxQuestionNumber",
                table: "BookImportPages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DuplicatesSkipped",
                table: "BookImportJobTests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QuestionsSaved",
                table: "BookImportJobTests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewFlagsJson",
                table: "BookImportJobTests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "MaxQuestionNumber",
                table: "BookImportPages");

            migrationBuilder.DropColumn(
                name: "DuplicatesSkipped",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "QuestionsSaved",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "ReviewFlagsJson",
                table: "BookImportJobTests");
        }
    }
}

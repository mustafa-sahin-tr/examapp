using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionSourceBox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoxHeight",
                table: "QuestionSources",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BoxWidth",
                table: "QuestionSources",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BoxX",
                table: "QuestionSources",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BoxY",
                table: "QuestionSources",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoxHeight",
                table: "QuestionSources");

            migrationBuilder.DropColumn(
                name: "BoxWidth",
                table: "QuestionSources");

            migrationBuilder.DropColumn(
                name: "BoxX",
                table: "QuestionSources");

            migrationBuilder.DropColumn(
                name: "BoxY",
                table: "QuestionSources");
        }
    }
}

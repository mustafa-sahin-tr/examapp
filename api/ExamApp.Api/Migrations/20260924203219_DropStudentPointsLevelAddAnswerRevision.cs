using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropStudentPointsLevelAddAnswerRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Level",
                table: "StudentPoints");

            migrationBuilder.AddColumn<int>(
                name: "AnswerRevision",
                table: "TestInstanceQuestions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswerRevision",
                table: "TestInstanceQuestions");

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "StudentPoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}

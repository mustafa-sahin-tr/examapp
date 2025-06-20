using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class CanvasQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCanvasQuestion",
                table: "Questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "X",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Y",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "Passage",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCanvasQuestion",
                table: "Passage",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "Passage",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "X",
                table: "Passage",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Y",
                table: "Passage",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "Answers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCanvasQuestion",
                table: "Answers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "Answers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "X",
                table: "Answers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Y",
                table: "Answers",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Height",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "IsCanvasQuestion",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "X",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Y",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "IsCanvasQuestion",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "X",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "Y",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "IsCanvasQuestion",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "X",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "Y",
                table: "Answers");
        }
    }
}

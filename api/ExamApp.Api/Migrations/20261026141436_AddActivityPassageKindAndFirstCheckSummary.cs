using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityPassageKindAndFirstCheckSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FirstCheckCorrectCount",
                table: "TestInstances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirstCheckTotalCount",
                table: "TestInstances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CheckedAnswerRevision",
                table: "TestInstanceQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instruction",
                table: "Passage",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Passage",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstCheckCorrectCount",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "FirstCheckTotalCount",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "CheckedAnswerRevision",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "Instruction",
                table: "Passage");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Passage");
        }
    }
}

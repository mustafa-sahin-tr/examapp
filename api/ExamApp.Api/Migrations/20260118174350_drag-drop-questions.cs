using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class dragdropquestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnswerPayload",
                table: "TestInstanceQuestions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InteractionPlan",
                table: "Questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InteractionType",
                table: "Questions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswerPayload",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "InteractionPlan",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "InteractionType",
                table: "Questions");
        }
    }
}

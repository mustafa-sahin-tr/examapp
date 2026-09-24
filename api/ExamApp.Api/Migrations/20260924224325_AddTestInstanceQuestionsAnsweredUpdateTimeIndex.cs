using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTestInstanceQuestionsAnsweredUpdateTimeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TestInstanceQuestions_UpdateTime_Answered",
                table: "TestInstanceQuestions",
                column: "UpdateTime",
                filter: "NOT \"IsDeleted\" AND (\"SelectedAnswerId\" IS NOT NULL OR \"AnswerPayload\" IS NOT NULL) AND \"UpdateTime\" IS NOT NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "WorksheetInstanceId", "IsCorrect", "TimeTaken" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestInstanceQuestions_UpdateTime_Answered",
                table: "TestInstanceQuestions");
        }
    }
}

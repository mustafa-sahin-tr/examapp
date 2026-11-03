using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                table: "QuestionTransferJobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentFingerprint",
                table: "Questions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Questions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ContentFingerprint",
                table: "Questions",
                column: "ContentFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ContentHash",
                table: "Questions",
                column: "ContentHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_ContentFingerprint",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_ContentHash",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                table: "QuestionTransferJobs");

            migrationBuilder.DropColumn(
                name: "ContentFingerprint",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Questions");
        }
    }
}

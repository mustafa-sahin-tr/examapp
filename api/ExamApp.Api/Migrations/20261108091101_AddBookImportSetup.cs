using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookImportSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnswerKey",
                table: "BookImportJobTests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BookTestId",
                table: "BookImportJobTests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpectedQuestions",
                table: "BookImportJobTests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FileFrom",
                table: "BookImportJobTests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FileTo",
                table: "BookImportJobTests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyConflicts",
                table: "BookImportJobTests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Minutes",
                table: "BookImportJobTests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AiCostUsd",
                table: "BookImportJobs",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "SetupJson",
                table: "BookImportJobs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookImportJobTests_BookTestId",
                table: "BookImportJobTests",
                column: "BookTestId");

            migrationBuilder.AddForeignKey(
                name: "FK_BookImportJobTests_BookTests_BookTestId",
                table: "BookImportJobTests",
                column: "BookTestId",
                principalTable: "BookTests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookImportJobTests_BookTests_BookTestId",
                table: "BookImportJobTests");

            migrationBuilder.DropIndex(
                name: "IX_BookImportJobTests_BookTestId",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "AnswerKey",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "BookTestId",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "ExpectedQuestions",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "FileFrom",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "FileTo",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "KeyConflicts",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "Minutes",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "AiCostUsd",
                table: "BookImportJobs");

            migrationBuilder.DropColumn(
                name: "SetupJson",
                table: "BookImportJobs");
        }
    }
}

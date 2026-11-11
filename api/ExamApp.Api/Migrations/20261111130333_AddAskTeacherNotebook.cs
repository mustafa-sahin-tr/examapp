using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAskTeacherNotebook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SavedToNotebookAt",
                table: "AskTeacherQuestions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AskTeacherQuestions_StudentUserId_SavedToNotebookAt",
                table: "AskTeacherQuestions",
                columns: new[] { "StudentUserId", "SavedToNotebookAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AskTeacherQuestions_StudentUserId_SavedToNotebookAt",
                table: "AskTeacherQuestions");

            migrationBuilder.DropColumn(
                name: "SavedToNotebookAt",
                table: "AskTeacherQuestions");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTopicPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TestInstances_StudentId_EndTime",
                table: "TestInstances",
                columns: new[] { "StudentId", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_StudentId_EndTime",
                table: "PracticeSessions",
                columns: new[] { "StudentId", "EndTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestInstances_StudentId_EndTime",
                table: "TestInstances");

            migrationBuilder.DropIndex(
                name: "IX_PracticeSessions_StudentId_EndTime",
                table: "PracticeSessions");
        }
    }
}

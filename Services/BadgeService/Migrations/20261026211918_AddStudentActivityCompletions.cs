using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentActivityCompletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentActivityCompletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ActivityId = table.Column<int>(type: "integer", nullable: false),
                    WorksheetInstanceId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    ActiveSeconds = table.Column<int>(type: "integer", nullable: false),
                    ItemCount = table.Column<int>(type: "integer", nullable: false),
                    AnsweredItemCount = table.Column<int>(type: "integer", nullable: false),
                    CountsTowardBadges = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivityDate = table.Column<DateTime>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentActivityCompletions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentActivityCompletions_EventId",
                table: "StudentActivityCompletions",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentActivityCompletions_UserId_ActivityDate",
                table: "StudentActivityCompletions",
                columns: new[] { "UserId", "ActivityDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentActivityCompletions_UserId_ActivityId",
                table: "StudentActivityCompletions",
                columns: new[] { "UserId", "ActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentActivityCompletions_UserId_WorksheetInstanceId",
                table: "StudentActivityCompletions",
                columns: new[] { "UserId", "WorksheetInstanceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentActivityCompletions");
        }
    }
}

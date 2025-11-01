using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class aggregatemodel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentBadgeProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    BadgeDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentValue = table.Column<int>(type: "integer", nullable: false),
                    TargetValue = table.Column<int>(type: "integer", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentBadgeProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentBadgeProgresses_BadgeDefinitions_BadgeDefinitionId",
                        column: x => x.BadgeDefinitionId,
                        principalTable: "BadgeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentDailyActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ActivityDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    QuestionCount = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    TotalTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    TotalPoints = table.Column<int>(type: "integer", nullable: false),
                    ActivityScore = table.Column<int>(type: "integer", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentDailyActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudentQuestionAggregates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TotalQuestions = table.Column<int>(type: "integer", nullable: false),
                    CorrectQuestions = table.Column<int>(type: "integer", nullable: false),
                    TotalTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    TotalPoints = table.Column<int>(type: "integer", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentQuestionAggregates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudentSubjectAggregates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    SubjectName = table.Column<string>(type: "text", nullable: false),
                    TotalQuestions = table.Column<int>(type: "integer", nullable: false),
                    CorrectQuestions = table.Column<int>(type: "integer", nullable: false),
                    TotalTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    TotalPoints = table.Column<int>(type: "integer", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentSubjectAggregates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentBadgeProgresses_BadgeDefinitionId",
                table: "StudentBadgeProgresses",
                column: "BadgeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentBadgeProgresses_UserId_BadgeDefinitionId",
                table: "StudentBadgeProgresses",
                columns: new[] { "UserId", "BadgeDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentDailyActivities_UserId_ActivityDate",
                table: "StudentDailyActivities",
                columns: new[] { "UserId", "ActivityDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentQuestionAggregates_UserId",
                table: "StudentQuestionAggregates",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjectAggregates_UserId_SubjectId",
                table: "StudentSubjectAggregates",
                columns: new[] { "UserId", "SubjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentBadgeProgresses");

            migrationBuilder.DropTable(
                name: "StudentDailyActivities");

            migrationBuilder.DropTable(
                name: "StudentQuestionAggregates");

            migrationBuilder.DropTable(
                name: "StudentSubjectAggregates");
        }
    }
}

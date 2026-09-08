using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PracticeSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    GradeId = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubjectIdsJson = table.Column<string>(type: "text", nullable: true),
                    TopicIdsJson = table.Column<string>(type: "text", nullable: true),
                    CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdateUserId = table.Column<int>(type: "integer", nullable: true),
                    DeleteTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeleteUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeSessions_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PracticeSessions_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PracticeSessionQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PracticeSessionId = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    ShownAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SelectedAnswerId = table.Column<int>(type: "integer", nullable: true),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    IsSkipped = table.Column<bool>(type: "boolean", nullable: false),
                    TimeTaken = table.Column<int>(type: "integer", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdateUserId = table.Column<int>(type: "integer", nullable: true),
                    DeleteTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeleteUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSessionQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeSessionQuestions_Answers_SelectedAnswerId",
                        column: x => x.SelectedAnswerId,
                        principalTable: "Answers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PracticeSessionQuestions_PracticeSessions_PracticeSessionId",
                        column: x => x.PracticeSessionId,
                        principalTable: "PracticeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PracticeSessionQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessionQuestions_PracticeSessionId_QuestionId",
                table: "PracticeSessionQuestions",
                columns: new[] { "PracticeSessionId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessionQuestions_QuestionId",
                table: "PracticeSessionQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessionQuestions_SelectedAnswerId",
                table: "PracticeSessionQuestions",
                column: "SelectedAnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_GradeId",
                table: "PracticeSessions",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_StudentId_Status",
                table: "PracticeSessions",
                columns: new[] { "StudentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PracticeSessionQuestions");

            migrationBuilder.DropTable(
                name: "PracticeSessions");
        }
    }
}

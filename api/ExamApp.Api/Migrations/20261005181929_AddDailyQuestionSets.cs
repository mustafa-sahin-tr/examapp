using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyQuestionSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyQuestionSets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    GradeId = table.Column<int>(type: "integer", nullable: false),
                    TargetCount = table.Column<int>(type: "integer", nullable: false),
                    ScopeJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PracticeSessionId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_DailyQuestionSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyQuestionSets_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyQuestionSets_PracticeSessions_PracticeSessionId",
                        column: x => x.PracticeSessionId,
                        principalTable: "PracticeSessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DailyQuestionSets_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyQuestionSetItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DailyQuestionSetId = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_DailyQuestionSetItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyQuestionSetItems_DailyQuestionSets_DailyQuestionSetId",
                        column: x => x.DailyQuestionSetId,
                        principalTable: "DailyQuestionSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyQuestionSetItems_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSetItems_DailyQuestionSetId_Order",
                table: "DailyQuestionSetItems",
                columns: new[] { "DailyQuestionSetId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSetItems_DailyQuestionSetId_QuestionId",
                table: "DailyQuestionSetItems",
                columns: new[] { "DailyQuestionSetId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSetItems_QuestionId",
                table: "DailyQuestionSetItems",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSets_GradeId",
                table: "DailyQuestionSets",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSets_PracticeSessionId",
                table: "DailyQuestionSets",
                column: "PracticeSessionId",
                unique: true,
                filter: "\"PracticeSessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSets_StudentId_Day",
                table: "DailyQuestionSets",
                columns: new[] { "StudentId", "Day" },
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyQuestionSetItems");

            migrationBuilder.DropTable(
                name: "DailyQuestionSets");
        }
    }
}

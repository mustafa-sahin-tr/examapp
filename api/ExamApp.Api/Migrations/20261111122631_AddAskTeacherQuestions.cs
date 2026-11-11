using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAskTeacherQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AskTeacherQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    StudentUserId = table.Column<int>(type: "integer", nullable: false),
                    TeacherId = table.Column<int>(type: "integer", nullable: false),
                    TeacherUserId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AnswerText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AnswerImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_AskTeacherQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AskTeacherQuestions_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AskTeacherQuestions_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AskTeacherQuestions_Teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SubscriptionPlanFeatureLimits",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "FeatureKey", "IsDeleted", "MonthlyLimit", "PlanId", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 91, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "ask-teacher", false, 10, 8, null, null },
                    { 92, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "ask-teacher", false, 40, 9, null, null },
                    { 93, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "ask-teacher", false, 40, 2, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AskTeacherQuestions_Status_DueAt",
                table: "AskTeacherQuestions",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AskTeacherQuestions_StudentId",
                table: "AskTeacherQuestions",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_AskTeacherQuestions_StudentUserId_CreatedAt",
                table: "AskTeacherQuestions",
                columns: new[] { "StudentUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AskTeacherQuestions_SubjectId",
                table: "AskTeacherQuestions",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AskTeacherQuestions_TeacherId",
                table: "AskTeacherQuestions",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_AskTeacherQuestions_TeacherUserId_Status_DueAt",
                table: "AskTeacherQuestions",
                columns: new[] { "TeacherUserId", "Status", "DueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AskTeacherQuestions");

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 91);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 92);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 93);
        }
    }
}

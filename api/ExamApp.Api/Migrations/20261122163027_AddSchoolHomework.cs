using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolHomework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchoolHomeworks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    TeacherUserId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    BookName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Pages = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Target = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetGradeId = table.Column<int>(type: "integer", nullable: true),
                    TargetSectionId = table.Column<int>(type: "integer", nullable: true),
                    TargetLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolHomeworks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchoolHomeworks_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SchoolHomeworks_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SchoolHomeworkProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HomeworkId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    StudentDoneAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TeacherStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    TeacherNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CheckedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolHomeworkProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchoolHomeworkProgress_SchoolHomeworks_HomeworkId",
                        column: x => x.HomeworkId,
                        principalTable: "SchoolHomeworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SchoolHomeworkProgress_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SchoolHomeworkStudents",
                columns: table => new
                {
                    HomeworkId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolHomeworkStudents", x => new { x.HomeworkId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_SchoolHomeworkStudents_SchoolHomeworks_HomeworkId",
                        column: x => x.HomeworkId,
                        principalTable: "SchoolHomeworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SchoolHomeworkStudents_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolHomeworkProgress_HomeworkId_StudentId",
                table: "SchoolHomeworkProgress",
                columns: new[] { "HomeworkId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchoolHomeworkProgress_StudentId",
                table: "SchoolHomeworkProgress",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolHomeworks_SchoolId_TargetGradeId_DueDate",
                table: "SchoolHomeworks",
                columns: new[] { "SchoolId", "TargetGradeId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolHomeworks_SubjectId",
                table: "SchoolHomeworks",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolHomeworks_TargetSectionId",
                table: "SchoolHomeworks",
                column: "TargetSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolHomeworks_TeacherUserId_CreatedAt",
                table: "SchoolHomeworks",
                columns: new[] { "TeacherUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolHomeworkStudents_StudentId",
                table: "SchoolHomeworkStudents",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchoolHomeworkProgress");

            migrationBuilder.DropTable(
                name: "SchoolHomeworkStudents");

            migrationBuilder.DropTable(
                name: "SchoolHomeworks");
        }
    }
}

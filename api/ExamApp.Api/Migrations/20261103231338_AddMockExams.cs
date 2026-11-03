using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMockExams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WrongAnswersPerCorrect = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_ExamTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamTemplateSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExamTemplateId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_ExamTemplateSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamTemplateSessions_ExamTemplates_ExamTemplateId",
                        column: x => x.ExamTemplateId,
                        principalTable: "ExamTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MockExams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExamTemplateId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GradeId = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_MockExams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockExams_ExamTemplates_ExamTemplateId",
                        column: x => x.ExamTemplateId,
                        principalTable: "ExamTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockExams_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamTemplateSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExamTemplateSessionId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QuestionCount = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_ExamTemplateSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamTemplateSections_ExamTemplateSessions_ExamTemplateSessi~",
                        column: x => x.ExamTemplateSessionId,
                        principalTable: "ExamTemplateSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MockExamSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MockExamId = table.Column<int>(type: "integer", nullable: false),
                    ExamTemplateSessionId = table.Column<int>(type: "integer", nullable: false),
                    WorksheetId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_MockExamSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockExamSessions_ExamTemplateSessions_ExamTemplateSessionId",
                        column: x => x.ExamTemplateSessionId,
                        principalTable: "ExamTemplateSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockExamSessions_MockExams_MockExamId",
                        column: x => x.MockExamId,
                        principalTable: "MockExams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockExamSessions_Worksheets_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "Worksheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MockExamQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MockExamSessionId = table.Column<int>(type: "integer", nullable: false),
                    ExamTemplateSectionId = table.Column<int>(type: "integer", nullable: false),
                    WorksheetQuestionId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_MockExamQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockExamQuestions_ExamTemplateSections_ExamTemplateSectionId",
                        column: x => x.ExamTemplateSectionId,
                        principalTable: "ExamTemplateSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockExamQuestions_MockExamSessions_MockExamSessionId",
                        column: x => x.MockExamSessionId,
                        principalTable: "MockExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MockExamQuestions_TestQuestions_WorksheetQuestionId",
                        column: x => x.WorksheetQuestionId,
                        principalTable: "TestQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ExamTemplates",
                columns: new[] { "Id", "Code", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "Name", "SourceReference", "UpdateTime", "UpdateUserId", "WrongAnswersPerCorrect" },
                values: new object[,]
                {
                    { 1, "lgs", new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "LGS", "T.C. MEB, Sınavla Öğrenci Alacak Ortaöğretim Kurumlarına İlişkin Merkezî Sınav Başvuru ve Uygulama Kılavuzu, 2026", null, null, 3 },
                    { 2, "tyt", new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "YKS TYT", "T.C. ÖSYM, 2026 Yükseköğretim Kurumları Sınavı (YKS) Kılavuzu, Tablo 1A ve 1B", null, null, 4 },
                    { 3, "ayt", new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "YKS AYT", "T.C. ÖSYM, 2026 Yükseköğretim Kurumları Sınavı (YKS) Kılavuzu, Tablo 1A ve 1B", null, null, 4 }
                });

            migrationBuilder.InsertData(
                table: "ExamTemplateSessions",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "DurationSeconds", "ExamTemplateId", "IsDeleted", "Name", "Order", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4500, 1, false, "Sözel Bölüm", 1, null, null },
                    { 2, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4800, 1, false, "Sayısal Bölüm", 2, null, null },
                    { 3, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 9900, 2, false, "Temel Yeterlilik Testi", 1, null, null },
                    { 4, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 10800, 3, false, "Alan Yeterlilik Testleri", 1, null, null }
                });

            migrationBuilder.InsertData(
                table: "ExamTemplateSections",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "ExamTemplateSessionId", "GroupName", "IsDeleted", "Name", "Order", "QuestionCount", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 1, null, false, "Türkçe", 1, 20, null, null },
                    { 2, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 1, null, false, "T.C. İnkılap Tarihi ve Atatürkçülük", 2, 10, null, null },
                    { 3, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 1, null, false, "Din Kültürü ve Ahlak Bilgisi", 3, 10, null, null },
                    { 4, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 1, null, false, "Yabancı Dil", 4, 10, null, null },
                    { 5, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 2, null, false, "Matematik", 1, 20, null, null },
                    { 6, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 2, null, false, "Fen Bilimleri", 2, 20, null, null },
                    { 7, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 3, null, false, "Türkçe", 1, 40, null, null },
                    { 8, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 3, "Sosyal Bilimler", false, "Tarih", 2, 5, null, null },
                    { 9, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 3, "Sosyal Bilimler", false, "Coğrafya", 3, 5, null, null },
                    { 10, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 3, "Sosyal Bilimler", false, "Felsefe", 4, 5, null, null },
                    { 11, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 3, "Sosyal Bilimler", false, "Din Kültürü ve Ahlak Bilgisi", 5, 5, null, null },
                    { 12, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 3, null, false, "Temel Matematik", 6, 40, null, null },
                    { 13, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 3, "Fen Bilimleri", false, "Fizik", 7, 7, null, null },
                    { 14, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 3, "Fen Bilimleri", false, "Kimya", 8, 7, null, null },
                    { 15, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 3, "Fen Bilimleri", false, "Biyoloji", 9, 6, null, null },
                    { 16, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, "Türk Dili ve Edebiyatı-Sosyal Bilimler-1", false, "Türk Dili ve Edebiyatı", 1, 24, null, null },
                    { 17, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, "Türk Dili ve Edebiyatı-Sosyal Bilimler-1", false, "Tarih-1", 2, 10, null, null },
                    { 18, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, "Türk Dili ve Edebiyatı-Sosyal Bilimler-1", false, "Coğrafya-1", 3, 6, null, null },
                    { 19, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, "Sosyal Bilimler-2", false, "Tarih-2", 4, 11, null, null },
                    { 20, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, "Sosyal Bilimler-2", false, "Coğrafya-2", 5, 11, null, null },
                    { 21, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, "Sosyal Bilimler-2", false, "Felsefe Grubu", 6, 12, null, null },
                    { 22, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, "Sosyal Bilimler-2", false, "Din Kültürü ve Ahlak Bilgisi", 7, 6, null, null },
                    { 23, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, null, false, "Matematik", 8, 40, null, null },
                    { 24, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, "Fen Bilimleri", false, "Fizik", 9, 14, null, null },
                    { 25, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, "Fen Bilimleri", false, "Kimya", 10, 13, null, null },
                    { 26, new DateTime(2026, 11, 4, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 4, "Fen Bilimleri", false, "Biyoloji", 11, 13, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamTemplates_Code",
                table: "ExamTemplates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamTemplateSections_ExamTemplateSessionId",
                table: "ExamTemplateSections",
                column: "ExamTemplateSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamTemplateSessions_ExamTemplateId",
                table: "ExamTemplateSessions",
                column: "ExamTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MockExamQuestions_ExamTemplateSectionId",
                table: "MockExamQuestions",
                column: "ExamTemplateSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_MockExamQuestions_MockExamSessionId",
                table: "MockExamQuestions",
                column: "MockExamSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MockExamQuestions_WorksheetQuestionId",
                table: "MockExamQuestions",
                column: "WorksheetQuestionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockExams_ExamTemplateId",
                table: "MockExams",
                column: "ExamTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MockExams_GradeId",
                table: "MockExams",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_MockExamSessions_ExamTemplateSessionId",
                table: "MockExamSessions",
                column: "ExamTemplateSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MockExamSessions_MockExamId",
                table: "MockExamSessions",
                column: "MockExamId");

            migrationBuilder.CreateIndex(
                name: "IX_MockExamSessions_WorksheetId",
                table: "MockExamSessions",
                column: "WorksheetId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MockExamQuestions");

            migrationBuilder.DropTable(
                name: "ExamTemplateSections");

            migrationBuilder.DropTable(
                name: "MockExamSessions");

            migrationBuilder.DropTable(
                name: "ExamTemplateSessions");

            migrationBuilder.DropTable(
                name: "MockExams");

            migrationBuilder.DropTable(
                name: "ExamTemplates");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningOutcomeVersionAndQuestionLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Issue #692: aynı kod iki kez varsa benzersiz index kurulamaz; hangisinin kalacağı elle seçilir.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "LearningOutcomes" WHERE NOT "IsDeleted" GROUP BY "Code" HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'LearningOutcomes contains duplicate codes; resolve them before this migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_LearningOutcomes_SubTopics_SubTopicId",
                table: "LearningOutcomes");

            migrationBuilder.AlterColumn<int>(
                name: "SubTopicId",
                table: "LearningOutcomes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "CurriculumVersion",
                table: "LearningOutcomes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "LearningOutcomes",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubjectId",
                table: "LearningOutcomes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuestionLearningOutcomes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    LearningOutcomeId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_QuestionLearningOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionLearningOutcomes_LearningOutcomes_LearningOutcomeId",
                        column: x => x.LearningOutcomeId,
                        principalTable: "LearningOutcomes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionLearningOutcomes_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearningOutcomes_CurriculumVersion_Code",
                table: "LearningOutcomes",
                columns: new[] { "CurriculumVersion", "Code" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_LearningOutcomes_GradeId_SubjectId",
                table: "LearningOutcomes",
                columns: new[] { "GradeId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_LearningOutcomes_SubjectId",
                table: "LearningOutcomes",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionLearningOutcomes_LearningOutcomeId",
                table: "QuestionLearningOutcomes",
                column: "LearningOutcomeId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionLearningOutcomes_QuestionId_LearningOutcomeId",
                table: "QuestionLearningOutcomes",
                columns: new[] { "QuestionId", "LearningOutcomeId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.AddForeignKey(
                name: "FK_LearningOutcomes_SubTopics_SubTopicId",
                table: "LearningOutcomes",
                column: "SubTopicId",
                principalTable: "SubTopics",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_LearningOutcomes_Subjects_SubjectId",
                table: "LearningOutcomes",
                column: "SubjectId",
                principalTable: "Subjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Issue #692: mevcut satırlarda sınıf boş (0), ders yok; alt konunun konusundan doldurulur.
            migrationBuilder.Sql("""
                UPDATE "LearningOutcomes" AS lo
                SET "GradeId" = t."GradeId", "SubjectId" = t."SubjectId"
                FROM "SubTopics" AS st
                JOIN "Topics" AS t ON t."Id" = st."TopicId"
                WHERE st."Id" = lo."SubTopicId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LearningOutcomes_SubTopics_SubTopicId",
                table: "LearningOutcomes");

            migrationBuilder.DropForeignKey(
                name: "FK_LearningOutcomes_Subjects_SubjectId",
                table: "LearningOutcomes");

            migrationBuilder.DropTable(
                name: "QuestionLearningOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_LearningOutcomes_CurriculumVersion_Code",
                table: "LearningOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_LearningOutcomes_GradeId_SubjectId",
                table: "LearningOutcomes");

            migrationBuilder.DropIndex(
                name: "IX_LearningOutcomes_SubjectId",
                table: "LearningOutcomes");

            migrationBuilder.DropColumn(
                name: "CurriculumVersion",
                table: "LearningOutcomes");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "LearningOutcomes");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "LearningOutcomes");

            migrationBuilder.AlterColumn<int>(
                name: "SubTopicId",
                table: "LearningOutcomes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LearningOutcomes_SubTopics_SubTopicId",
                table: "LearningOutcomes",
                column: "SubTopicId",
                principalTable: "SubTopics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

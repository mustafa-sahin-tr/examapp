using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorksheetCommentModerationAndSchoolScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthorSchoolId",
                table: "WorksheetComments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenAt",
                table: "WorksheetComments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HiddenByUserId",
                table: "WorksheetComments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenReason",
                table: "WorksheetComments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // issue #305 (veri): mevcut öğrenci yorumlarına yazarın okulunu sabitle — okul kapsamı okuma anında join'siz
            // bu kolonla uygulanır. Kaynak yazarın canlı Students satırı (UserId başına tek canlı satır, #259 unique index).
            // Canlı satırı olmayan ya da okulsuz yazarın yorumu null kalır (güvenli taraf: yalnız yazarı + sorumlu öğretmen
            // görür). Öğretmen yorumları null kalır (herkese görünür). Idempotent (yalnız null satırlar).
            migrationBuilder.Sql("""
                UPDATE "WorksheetComments" AS c
                SET "AuthorSchoolId" = s."SchoolId"
                FROM "Students" AS s
                WHERE c."AuthorRole" = 'Student'
                  AND c."AuthorSchoolId" IS NULL
                  AND s."UserId" = c."AuthorUserId"
                  AND NOT s."IsDeleted"
                  AND s."SchoolId" IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "WorksheetCommentReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    ReporterUserId = table.Column<int>(type: "integer", nullable: false),
                    ReporterKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_WorksheetCommentReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorksheetCommentReports_WorksheetComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "WorksheetComments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetCommentReports_CommentId_ReporterUserId",
                table: "WorksheetCommentReports",
                columns: new[] { "CommentId", "ReporterUserId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorksheetCommentReports");

            migrationBuilder.DropColumn(
                name: "AuthorSchoolId",
                table: "WorksheetComments");

            migrationBuilder.DropColumn(
                name: "HiddenAt",
                table: "WorksheetComments");

            migrationBuilder.DropColumn(
                name: "HiddenByUserId",
                table: "WorksheetComments");

            migrationBuilder.DropColumn(
                name: "HiddenReason",
                table: "WorksheetComments");
        }
    }
}

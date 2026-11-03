using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Topics",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "SubTopics",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Subjects",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Grades",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Topics_Code",
                table: "Topics",
                column: "Code",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_SubTopics_Code",
                table: "SubTopics",
                column: "Code",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Code",
                table: "Subjects",
                column: "Code",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_Grades_Code",
                table: "Grades",
                column: "Code",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            // Mevcut satırlara koddan bağımsız, addan türeyen kod verilir: aynı veriye sahip iki ortam aynı kodu alır.
            // Slug kuralı CatalogCode.Slug ile aynıdır. Aynı slug'a düşen satır, Id sırasıyla -2, -3 eki alır.
            migrationBuilder.Sql("""
                CREATE FUNCTION pg_temp.catalog_slug(value text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
                    SELECT trim(both '-' from left(
                        trim(both '-' from regexp_replace(
                            lower(translate(value, 'ÇçĞğİıÖöŞşÜüÂâÎîÛû', 'ccggiioossuuaaiiuu')),
                            '[^a-z0-9]+', '-', 'g')),
                        120))
                $$;

                UPDATE "Grades" AS target SET "Code" = ranked.code
                FROM (
                    SELECT "Id", base || CASE WHEN rn > 1 THEN '-' || rn ELSE '' END AS code
                    FROM (
                        SELECT "Id", base, row_number() OVER (PARTITION BY base ORDER BY "IsDeleted", "Id") AS rn
                        FROM (SELECT "Id", "IsDeleted", coalesce(nullif(pg_temp.catalog_slug("Name"), ''), 'grade-' || "Id") AS base FROM "Grades") AS named
                    ) AS numbered
                ) AS ranked
                WHERE target."Id" = ranked."Id";

                UPDATE "Subjects" AS target SET "Code" = ranked.code
                FROM (
                    SELECT "Id", base || CASE WHEN rn > 1 THEN '-' || rn ELSE '' END AS code
                    FROM (
                        SELECT "Id", base, row_number() OVER (PARTITION BY base ORDER BY "IsDeleted", "Id") AS rn
                        FROM (SELECT "Id", "IsDeleted", coalesce(nullif(pg_temp.catalog_slug("Name"), ''), 'subject-' || "Id") AS base FROM "Subjects") AS named
                    ) AS numbered
                ) AS ranked
                WHERE target."Id" = ranked."Id";

                UPDATE "Topics" AS target SET "Code" = ranked.code
                FROM (
                    SELECT "Id", base || CASE WHEN rn > 1 THEN '-' || rn ELSE '' END AS code
                    FROM (
                        SELECT "Id", base, row_number() OVER (PARTITION BY base ORDER BY "IsDeleted", "Id") AS rn
                        FROM (
                            SELECT t."Id", t."IsDeleted",
                                   g."Code" || '.' || s."Code" || '.' || coalesce(nullif(pg_temp.catalog_slug(t."Name"), ''), 'topic-' || t."Id") AS base
                            FROM "Topics" AS t
                            JOIN "Grades" AS g ON g."Id" = t."GradeId"
                            JOIN "Subjects" AS s ON s."Id" = t."SubjectId"
                        ) AS named
                    ) AS numbered
                ) AS ranked
                WHERE target."Id" = ranked."Id";

                UPDATE "SubTopics" AS target SET "Code" = ranked.code
                FROM (
                    SELECT "Id", base || CASE WHEN rn > 1 THEN '-' || rn ELSE '' END AS code
                    FROM (
                        SELECT "Id", base, row_number() OVER (PARTITION BY base ORDER BY "IsDeleted", "Id") AS rn
                        FROM (
                            SELECT st."Id", st."IsDeleted",
                                   t."Code" || '.' || coalesce(nullif(pg_temp.catalog_slug(st."Name"), ''), 'subtopic-' || st."Id") AS base
                            FROM "SubTopics" AS st
                            JOIN "Topics" AS t ON t."Id" = st."TopicId"
                        ) AS named
                    ) AS numbered
                ) AS ranked
                WHERE target."Id" = ranked."Id";

                DROP FUNCTION pg_temp.catalog_slug(text);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Topics_Code",
                table: "Topics");

            migrationBuilder.DropIndex(
                name: "IX_SubTopics_Code",
                table: "SubTopics");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_Code",
                table: "Subjects");

            migrationBuilder.DropIndex(
                name: "IX_Grades_Code",
                table: "Grades");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "SubTopics");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Grades");
        }
    }
}

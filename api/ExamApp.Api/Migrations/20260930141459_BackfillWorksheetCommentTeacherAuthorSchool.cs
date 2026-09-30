using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class BackfillWorksheetCommentTeacherAuthorSchool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // issue #305 review (security Y1, veri): öğretmen yorumlarına da yazarın okulunu sabitle — öğrenci okuyucu
            // öğretmen yorumunu okul eşleşmesiyle görür. Kaynak yazarın canlı Teachers satırı (UserId başına tek canlı satır,
            // #259 unique index). Bağımsız/okulsuz ya da canlı satırı olmayan öğretmenin yorumu null kalır. Önceki migration
            // yerelde uygulanmış olduğundan ayrı veri migration'ı. Idempotent (yalnız null satırlar).
            migrationBuilder.Sql("""
                UPDATE "WorksheetComments" AS c
                SET "AuthorSchoolId" = t."SchoolId"
                FROM "Teachers" AS t
                WHERE c."AuthorRole" = 'Teacher'
                  AND c."AuthorSchoolId" IS NULL
                  AND t."UserId" = c."AuthorUserId"
                  AND NOT t."IsDeleted"
                  AND t."SchoolId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Bilinçli no-op: kolon önceki migration'ın; geri alınırsa o migration'ın Down'u düşürür.
        }
    }
}

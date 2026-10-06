using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueTestInstancePerStudentWorksheet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // issue #367 — elle eklenen ön kontrol (üretilen DropIndex/CreateIndex çağrıları değiştirilmedi; #259 emsali).
            // Aynı (StudentId, WorksheetId) için birden fazla canlı instance varsa (eski "tekrar çözüm" açığıyla açılmış)
            // migration açık bir hatayla durur; veri silinmez/birleştirilmez — hangisinin kalacağı ürün kararıdır.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    dup_pairs text;
                BEGIN
                    SELECT count(*) || ' (StudentId/WorksheetId: ' || string_agg("StudentId" || '/' || "WorksheetId", ', ' ORDER BY "StudentId", "WorksheetId") || ')'
                      INTO dup_pairs
                      FROM (SELECT "StudentId", "WorksheetId" FROM "TestInstances" WHERE NOT "IsDeleted"
                            GROUP BY "StudentId", "WorksheetId" HAVING count(*) > 1) d;
                    IF dup_pairs IS NOT NULL THEN
                        RAISE EXCEPTION 'issue #367: ayni ogrenci/worksheet icin birden fazla canli TestInstances satiri var, unique index olusturulmadi: %', dup_pairs
                            USING HINT = 'Ciftleri elle inceleyip fazlalari soft-delete edin (IsDeleted = true), sonra database update komutunu tekrar calistirin.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_TestInstances_StudentId",
                table: "TestInstances");

            migrationBuilder.CreateIndex(
                name: "IX_TestInstances_StudentId_WorksheetId",
                table: "TestInstances",
                columns: new[] { "StudentId", "WorksheetId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestInstances_StudentId_WorksheetId",
                table: "TestInstances");

            migrationBuilder.CreateIndex(
                name: "IX_TestInstances_StudentId",
                table: "TestInstances",
                column: "StudentId");
        }
    }
}

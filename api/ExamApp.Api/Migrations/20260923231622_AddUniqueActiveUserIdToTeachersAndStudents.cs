using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueActiveUserIdToTeachersAndStudents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // issue #259 — elle eklenen ön kontrol (CreateIndex çağrıları üretilmiş haliyle). Canlı (silinmemiş) çift
            // UserId satırı varsa migration AÇIK bir hatayla durur; migration transaction'ı geri alınır, şema/veri
            // değişmez. Bilinçli olarak: (1) veri SİLİNMEZ/birleştirilmez — hangi satırın doğru olduğu (okul, onay
            // durumu, bağlı atamalar) ürün/insan kararıdır; (2) index "atlanmaz" — atlamak kilidi sessizce korumasız
            // bırakır ve model snapshot ile DB'yi ortamdan ortama farklılaştırır (deterministik değil). Çıplak
            // CREATE UNIQUE INDEX de durur ama yalnız ilk çakışan anahtarı söyler; bu kontrol tüm listeyi verir.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    dup_teachers text;
                    dup_students text;
                BEGIN
                    SELECT count(*) || ' (UserId: ' || string_agg("UserId"::text, ', ' ORDER BY "UserId") || ')'
                      INTO dup_teachers
                      FROM (SELECT "UserId" FROM "Teachers" WHERE NOT "IsDeleted"
                            GROUP BY "UserId" HAVING count(*) > 1) d;
                    SELECT count(*) || ' (UserId: ' || string_agg("UserId"::text, ', ' ORDER BY "UserId") || ')'
                      INTO dup_students
                      FROM (SELECT "UserId" FROM "Students" WHERE NOT "IsDeleted"
                            GROUP BY "UserId" HAVING count(*) > 1) d;
                    IF dup_teachers IS NOT NULL OR dup_students IS NOT NULL THEN
                        RAISE EXCEPTION 'issue #259: canli cift UserId satirlari var, unique index olusturulmadi. Teachers: %; Students: %',
                            coalesce(dup_teachers, '0'), coalesce(dup_students, '0')
                            USING HINT = 'Ciftleri elle inceleyip fazlalari soft-delete edin (IsDeleted = true), sonra database update komutunu tekrar calistirin.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Teachers_UserId",
                table: "Teachers",
                column: "UserId",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_Students_UserId",
                table: "Students",
                column: "UserId",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            // issue #259 (security review): backfill (BackfillGradeAssignmentSchoolIdFromCreatorTeacher) çift aktif
            // Teachers satırı olan öğretmenlerin atamalarını belirsiz diye atlar. Buraya ulaşıldıysa yukarıdaki kontrol
            // çift kalmadığını garanti etti → aynı idempotent backfill bir kez daha çalışır ve atlananları da kapsar.
            migrationBuilder.Sql(BackfillGradeAssignmentSchoolIdFromCreatorTeacher.BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Teachers_UserId",
                table: "Teachers");

            migrationBuilder.DropIndex(
                name: "IX_Students_UserId",
                table: "Students");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTestInstanceQuestionsAnsweredUpdateTimeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // issue #265 review — ELLE DÜZENLENDİ (precedent: #259/#279/#287 migration'ları). Üretilen CreateIndex düz
            // CREATE INDEX idi: migration transaction'ı içinde TestInstanceQuestions'a SHARE kilidi alır ve index bitene
            // kadar cevap kaydetmeyi (UPDATE) bloklar. Yerine aynı tanımla CREATE INDEX CONCURRENTLY (yazmaları bloklamaz).
            // Tanım AppDbContext'teki model yapılandırmasıyla BİREBİR aynıdır (ad, kolon, INCLUDE, WHERE) — snapshot
            // değişmedi, has-pending-model-changes temiz kalır.
            //
            // CONCURRENTLY transaction içinde çalışamaz → suppressTransaction: true. Uygulama yolu: Program.cs açılışta
            // context.Database.Migrate() (ayrı migration servisi yok; Aspire de aynı Program.cs'i çalıştırır). EF bu komuttan
            // önce açık migration transaction'ını commit eder, komutu transaction dışında çalıştırır, sonrakiler için yeni
            // transaction açar. Npgsql'in migration kilidi transaction kapsamlı (LockReleaseBehavior.Transaction); EF
            // transaction yeniden başlayınca kilidi IMigrationsDatabaseLock.ReacquireIfNeeded ile yeniden alır.
            //
            // Yarıda kalan CONCURRENTLY build'i INVALID bir index bırakır; IF NOT EXISTS onu "var" sayıp atlardı. Bu yüzden
            // önce (normal transaction'da) yalnızca GEÇERSİZ kalıntı düşürülür; geçerli index varsa (ör. history satırı
            // yazılmadan süreç öldüyse) create atlanır — tekrar çalıştırılabilir.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
                               WHERE c.relname = 'IX_TestInstanceQuestions_UpdateTime_Answered' AND NOT i.indisvalid) THEN
                        DROP INDEX "IX_TestInstanceQuestions_UpdateTime_Answered";
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_TestInstanceQuestions_UpdateTime_Answered"
                    ON "TestInstanceQuestions" ("UpdateTime")
                    INCLUDE ("WorksheetInstanceId", "IsCorrect", "TimeTaken")
                    WHERE NOT "IsDeleted" AND ("SelectedAnswerId" IS NOT NULL OR "AnswerPayload" IS NOT NULL) AND "UpdateTime" IS NOT NULL;
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // issue #265 review — elle: Up ile simetrik, yazmaları bloklamadan düşür (transaction dışında).
            migrationBuilder.Sql("""
                DROP INDEX CONCURRENTLY IF EXISTS "IX_TestInstanceQuestions_UpdateTime_Answered";
                """, suppressTransaction: true);
        }
    }
}

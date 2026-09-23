using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <summary>
    /// issue #236 (#222 takibi) — tek seferlik VERİ düzeltmesi, şema değişikliği yok (Up/Down gövdesi elle yazıldı).
    /// <para>
    /// #222 öncesi okulsuz (bağımsız) öğretmenin yazdığı <c>SchoolId=null</c> sınıf-hedefli atamalar, öğrenci tarafında
    /// o sınıftaki TÜM okulların öğrencilerine görünüyordu. Bu satırlar soft-delete edilir (global
    /// <c>!IsDeleted</c> filtresi → liste, test başlatma, takvim, hatırlatıcı oluşturma, sıralama, öğretmen görünümü
    /// dahil hiçbir yolda görünmez). <c>EndAt</c>'i şimdiye çekmek yetmez: takvim "son tarih" olayları ve
    /// hatırlatıcı/sıralama kontrolü (<c>AssignmentVisibleTo</c>) zaman penceresine bakmaz — EndAt=now bütün okulların
    /// öğrencilerine "bugün son gün" olayı üretirdi.
    /// </para>
    /// <para>
    /// Kriter: öğrenci hedefi yok + sınıf hedefi var + <c>SchoolId IS NULL</c> + oluşturan kullanıcının
    /// <c>Teachers</c> satırı okulsuz (ve okullu, silinmemiş bir Teachers satırı yok). Admin rolü yalnızca Keycloak'ta;
    /// exam DB'de admin'i ayıran kolon yok. Admin hesaplarının Teachers satırı olmadığı için "okulsuz Teachers satırı"
    /// öğretmen/admin ayrımının SQL vekilidir → admin'in platform geneli grade atamaları korunur. #234 sonrası
    /// onay bekleyen okullu öğretmen de (SchoolId=null + RequestedSchoolId) kapsama girer: çalışma zamanında
    /// <c>SchoolScope.IsIndependent</c> sayılır ve #222 kuralına tabidir.
    /// </para>
    /// <para>
    /// Bu atamalara dayanarak kurulmuş, henüz gönderilmemiş (Pending) hatırlatıcılar da iptal edilir (Status=Cancelled;
    /// dispatcher Cancelled'ı no-op geçer) — öğrencinin o worksheet'e başka görünür ataması veya instance'ı yoksa
    /// (<c>WorksheetReminderService.EnsureStudentCanAccessWorksheetAsync</c> ile aynı erişim kuralı).
    /// </para>
    /// İdempotent: ikinci çalıştırmada kriter silinmiş satırları dışarıda bırakır, no-op olur.
    /// </summary>
    public partial class SoftDeleteIndependentTeacherGradeAssignments : Migration
    {
        // Bu migration'ın soft-delete ettiği satırların ayırt edici işareti (gerçek kullanıcı id'leri > 0). Down bunu geri alır.
        private const int DeleteMarker = -236;

        // Düzeltilecek atamalar. Tek tanım — iki ifade de bunu kullanır.
        // Teachers koşullarındaki asimetri BİLİNÇLİ: EXISTS silinmiş okulsuz satırı da sayar — öğretmen kaydı sonradan
        // soft-delete edilmiş olsa da atamayı okulsuz bir öğretmen yazmıştır ve sızıntı sürer (silinmiş satır da
        // "admin değil, öğretmen" kanıtıdır). NOT EXISTS ise yalnızca CANLI okullu satırı koruma sayar: silinmiş bir
        // okullu kayıt kullanıcıyı bugün okullu yapmaz, silinmemiş okullu satırı olan belirsiz çok-satırlı kullanıcı korunur.
        private const string LeakedGradeAssignments = """
            SELECT a."Id", a."WorksheetId", a."GradeId"
            FROM "WorksheetAssignments" a
            WHERE NOT a."IsDeleted"
              AND a."StudentId" IS NULL
              AND a."GradeId" IS NOT NULL
              AND a."SchoolId" IS NULL
              AND a."CreateUserId" IS NOT NULL AND a."CreateUserId" > 0
              AND EXISTS (SELECT 1 FROM "Teachers" t
                          WHERE t."UserId" = a."CreateUserId" AND t."SchoolId" IS NULL)
              AND NOT EXISTS (SELECT 1 FROM "Teachers" t
                              WHERE t."UserId" = a."CreateUserId" AND t."SchoolId" IS NOT NULL AND NOT t."IsDeleted")
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Önce hatırlatıcılar: atamalar henüz silinmemişken "başka görünür erişim var mı" kontrolü
            //    düzeltilecek satırları hariç tutarak yapılır (aynı ifadede CTE etkisi görünmediği için sıra önemli).
            migrationBuilder.Sql($"""
                WITH fixed AS ({LeakedGradeAssignments})
                UPDATE "WorksheetReminders" r
                SET "Status" = 2, "HangfireJobId" = NULL, "UpdateTime" = now()
                FROM "Students" s
                WHERE s."Id" = r."StudentId"
                  AND r."Status" = 0
                  AND NOT r."IsDeleted"
                  AND EXISTS (SELECT 1 FROM fixed f
                              WHERE f."WorksheetId" = r."WorksheetId" AND f."GradeId" = s."GradeId")
                  AND NOT EXISTS (SELECT 1 FROM "WorksheetAssignments" a2
                                  WHERE NOT a2."IsDeleted"
                                    AND a2."WorksheetId" = r."WorksheetId"
                                    AND a2."Id" NOT IN (SELECT f2."Id" FROM fixed f2)
                                    AND (a2."StudentId" = s."Id"
                                         OR (a2."StudentId" IS NULL AND a2."GradeId" IS NOT NULL
                                             AND a2."GradeId" = s."GradeId"
                                             AND (a2."SchoolId" IS NULL OR a2."SchoolId" = s."SchoolId"))))
                  AND NOT EXISTS (SELECT 1 FROM "TestInstances" ti
                                  WHERE NOT ti."IsDeleted"
                                    AND ti."WorksheetId" = r."WorksheetId" AND ti."StudentId" = s."Id");
                """);

            // 2) Sızan sınıf atamalarını soft-delete et (EndAt/diğer alanlar korunur); DeleteUserId = işaret → Down geri alır.
            migrationBuilder.Sql($"""
                UPDATE "WorksheetAssignments"
                SET "IsDeleted" = TRUE, "DeleteTime" = now(), "DeleteUserId" = {DeleteMarker}
                WHERE "Id" IN (SELECT "Id" FROM ({LeakedGradeAssignments}) fixed);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Yalnızca bu migration'ın işaretlediği atamalar geri açılır (sızıntı yeniden açılır — bilinçli geri alma).
            // Hatırlatıcı iptali GERİ ALINMAZ: hangi Pending satırların bu migration ile Cancelled yapıldığı işaretlenmez
            // ve HangfireJobId temizlendiği için zamanlanmış job bağı zaten kopmuştur; öğrenci hatırlatıcıyı yeniden kurabilir.
            migrationBuilder.Sql($"""
                UPDATE "WorksheetAssignments"
                SET "IsDeleted" = FALSE, "DeleteTime" = NULL, "DeleteUserId" = NULL
                WHERE "DeleteUserId" = {DeleteMarker};
                """);
        }
    }
}

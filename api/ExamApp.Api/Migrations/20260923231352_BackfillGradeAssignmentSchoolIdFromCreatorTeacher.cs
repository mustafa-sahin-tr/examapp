using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <summary>
    /// issue #259 (#236 takibi) — tek seferlik VERİ düzeltmesi, şema değişikliği yok (Up/Down gövdesi elle yazıldı).
    /// <para>
    /// #12 (atamada SchoolId kolonu) öncesi okullu öğretmenlerin ve sonradan okula katılmış eski bağımsız öğretmenlerin
    /// sınıf-hedefli atamaları <c>SchoolId=null</c> kaldı; öğrenci tarafında (<c>AssignmentVisibleTo</c>) null okul
    /// "kısıt yok" demek olduğundan bu atamalar o sınıftaki TÜM okulların öğrencilerine görünüyor/başlatılabiliyordu.
    /// Oluşturanın bugünkü okulu atamaya yazılır → yalnız o okulun öğrencileri görür.
    /// </para>
    /// <para>
    /// Kriter: öğrenci hedefi yok + sınıf hedefi var + <c>SchoolId IS NULL</c> + oluşturanın (<c>CreateUserId</c>)
    /// TEK bir silinmemiş <c>Teachers</c> satırı var ve o satır okullu. Belirsiz durumlar DOKUNULMADAN kalır:
    /// <list type="bullet">
    /// <item>Birden fazla silinmemiş Teachers satırı (UserId bu migration anında unique değil) — hangi okul olduğu
    /// belirsiz; manuel inceleme.</item>
    /// <item>Okulsuz (bağımsız / onay bekleyen) öğretmen — #236 migration'ı bunları zaten soft-delete etti.</item>
    /// <item>Teachers satırı olmayan oluşturan — admin varsayımı (admin rolü yalnız Keycloak'ta; exam DB'de admin'i
    /// ayıran kolon yok): platform geneli grade atamaları bilinçli olarak null kalır. Admin'in okullu bir Teachers
    /// satırı varsa atama o okula DARALTILIR — güvenli yön (sızıntı değil, en fazla görünürlük kaybı).</item>
    /// </list>
    /// Silinmiş atamalar da kapsamda: yeniden açılırlarsa (ör. #236 Down) sızıntı geri gelmesin.
    /// Güncellenen satırlar <c>UpdateUserId = -259</c> ile işaretlenir (gerçek kullanıcı id'leri &gt; 0) — yalnız iz için.
    /// </para>
    /// İdempotent: ikinci çalıştırmada güncellenmiş satırlar <c>SchoolId IS NULL</c> koşulundan çıkar, no-op olur.
    /// </summary>
    public partial class BackfillGradeAssignmentSchoolIdFromCreatorTeacher : Migration
    {
        private const int UpdateMarker = -259;

        /// <summary>
        /// Backfill ifadesi. <c>AddUniqueActiveUserIdToTeachersAndStudents</c> bunu index oluşturulduktan SONRA bir kez
        /// daha çalıştırır: burada çift aktif Teachers satırı yüzünden atlanan atamalar, çiftler giderilip (o migration
        /// çift varken durur) index kurulduğunda düzeltilir. İdempotent olduğu için ikinci çalıştırma güvenli.
        /// </summary>
        internal static readonly string BackfillSql = $"""
                UPDATE "WorksheetAssignments" a
                SET "SchoolId" = t."SchoolId", "UpdateTime" = now(), "UpdateUserId" = {UpdateMarker}
                FROM "Teachers" t
                WHERE a."SchoolId" IS NULL
                  AND a."GradeId" IS NOT NULL
                  AND a."StudentId" IS NULL
                  AND a."CreateUserId" IS NOT NULL AND a."CreateUserId" > 0
                  AND t."UserId" = a."CreateUserId"
                  AND NOT t."IsDeleted"
                  AND t."SchoolId" IS NOT NULL
                  AND (SELECT count(*) FROM "Teachers" t2
                       WHERE t2."UserId" = a."CreateUserId" AND NOT t2."IsDeleted") = 1;
                """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Bilinçli no-op: bu bir güvenlik veri düzeltmesidir. SchoolId'yi null'a geri çekmek atamaları tekrar tüm
            // okulların öğrencilerine açar (sızıntıyı geri getirir). Geri alma gerekirse UpdateUserId = -259 izi ile
            // satırlar bulunabilir; bu karar bilinçli ve elle verilmelidir.
        }
    }
}

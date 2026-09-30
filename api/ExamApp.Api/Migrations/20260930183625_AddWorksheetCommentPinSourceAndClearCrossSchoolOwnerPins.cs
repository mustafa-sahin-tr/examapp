using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <summary>
    /// issue #326 (O2 + security O1): <c>WorksheetComments.ResponsibleTeacherSource</c> kolonu (AddColumn/DropColumn EF
    /// tarafından üretildi) + veri adımları (SQL elle eklendi; #259/#305 veri migration'larıyla aynı desen). Sırası:
    /// <list type="number">
    /// <item><see cref="BackfillSourceSql"/> — sabitli eski satırlara kaynak (en iyi tahmin).</item>
    /// <item><see cref="BackfillLegacyAuthorSchoolSql"/> — sahip kaynaklı sabitli, <c>AuthorSchoolId</c>'si NULL öğrenci
    /// satırlarında yazarın güncel okulu sahibinkiyle AYNIYSA yazar okulu sabitlenir (aksi halde okuma anındaki
    /// <c>SameSchool(okuyucu, AuthorSchoolId)</c> koşulu korunan sabiti yine de geçersiz kılardı).</item>
    /// <item><see cref="ClearSql"/> — okul dışı / okulsuz sahibe sabitlenmiş satırlarda sabit ve kaynak NULL.</item>
    /// </list>
    /// "Güncel okul" her yerde <c>UserSchoolResolver</c> kuralıyla (<see cref="LiveSchool"/>): canlı Teachers satırı varsa o
    /// (tam olarak tek canlı satır; değilse NULL), yoksa canlı Students satırı (tek değilse NULL). Hepsi idempotent.
    /// <c>Down()</c>: kolon düşer; temizlenen sabitler geri kurulmaz (hangi satırın temizlendiği tutulmuyor ve geri kurmak
    /// izolasyon açığını, #160, geri getirir).
    /// </summary>
    public partial class AddWorksheetCommentPinSourceAndClearCrossSchoolOwnerPins : Migration
    {
        /// <summary>
        /// <paramref name="userIdExpr"/> kullanıcısının güncel okulu — <c>UserSchoolResolver</c>'ın SQL karşılığı: canlı
        /// öğretmen satırı varsa o (okulsuzsa NULL, öğrenci satırına düşülmez), yoksa canlı öğrenci satırı; tam olarak tek canlı
        /// satır yoksa NULL (belirsiz → okulsuz).
        /// </summary>
        private static string LiveSchool(string userIdExpr) => $"""
            (CASE WHEN EXISTS (SELECT 1 FROM "Teachers" AS lt WHERE lt."UserId" = {userIdExpr} AND NOT lt."IsDeleted")
                  THEN (SELECT CASE WHEN count(*) = 1 THEN max(lt."SchoolId") END
                        FROM "Teachers" AS lt WHERE lt."UserId" = {userIdExpr} AND NOT lt."IsDeleted")
                  ELSE (SELECT CASE WHEN count(*) = 1 THEN max(ls."SchoolId") END
                        FROM "Students" AS ls WHERE ls."UserId" = {userIdExpr} AND NOT ls."IsDeleted")
             END)
            """;

        /// <summary>
        /// Sabit öğretmenin bu worksheet'te yorum yazarını (canlı Students satırı) hedefleyen ve YORUM ANINDA AKTİF olan bir
        /// ataması var mı — <c>WorksheetStudentAccess.IsAssignmentVisibleTo</c> hedef koşulu + yorum anında aktiflik
        /// (<c>StartAt &lt;= CreateTime</c>, <c>EndAt</c> yok ya da sonra, silinmemiş ya da <c>DeleteTime</c> sonra; silme
        /// zamanı bilinmeyen silinmiş satır sayılmaz). Yorumdan önce bitmiş/silinmiş ya da sonradan açılmış atama okul dışı
        /// sahibe Assignment etiketi veremez (security yeniden inceleme).
        /// </summary>
        private const string AssignmentLink = """
            EXISTS (
                SELECT 1
                FROM "WorksheetAssignments" AS a
                JOIN "Students" AS s ON s."UserId" = c."AuthorUserId" AND NOT s."IsDeleted"
                WHERE a."WorksheetId" = c."WorksheetId"
                  AND a."CreateUserId" = c."ResponsibleTeacherUserId"
                  AND a."StartAt" <= c."CreateTime"
                  AND (a."EndAt" IS NULL OR a."EndAt" > c."CreateTime")
                  AND (a."DeleteTime" IS NULL OR a."DeleteTime" > c."CreateTime")
                  AND (NOT a."IsDeleted" OR a."DeleteTime" IS NOT NULL)
                  AND (a."StudentId" = s."Id"
                       OR (a."StudentId" IS NULL AND a."GradeId" IS NOT NULL AND a."GradeId" = s."GradeId"
                           AND (a."IsPlatformWide" OR (a."SchoolId" IS NOT NULL AND a."SchoolId" = s."SchoolId")))))
            """;

        /// <summary>
        /// Kaynak tahmini (yalnız kaynağı boş sabitli satırlar): sabit worksheet sahibi değilse (ya da sahipsiz worksheet) başka
        /// yoldan gelemez → Assignment; sahipse ve sahibin yazarı hedefleyen, YORUM ANINDA AKTİF ataması varsa → Assignment (sabit atamadan gelmiş
        /// olabilir, #105 ilişkisi koparılmaz); aksi halde kopyada CopyOwner, değilse Owner.
        /// </summary>
        public static readonly string BackfillSourceSql = $"""
            UPDATE "WorksheetComments" AS c
            SET "ResponsibleTeacherSource" = CASE
                    WHEN w."CreateUserId" IS NULL OR c."ResponsibleTeacherUserId" <> w."CreateUserId" OR {AssignmentLink}
                        THEN 'Assignment'
                    WHEN w."SourceWorksheetId" IS NOT NULL THEN 'CopyOwner'
                    ELSE 'Owner'
                END
            FROM "Worksheets" AS w
            WHERE w."Id" = c."WorksheetId"
              AND c."ResponsibleTeacherUserId" IS NOT NULL
              AND c."ResponsibleTeacherSource" IS NULL;
            """;

        /// <summary>
        /// Code review Orta 2: <c>AuthorSchoolId</c>'si NULL (legacy) sahip kaynaklı sabitli öğrenci satırı — yazarın güncel okulu
        /// sahibin güncel okuluyla aynıysa (ikisi de dolu) yazar okulu o okul olarak sabitlenir; sabit korunur.
        /// </summary>
        public static readonly string BackfillLegacyAuthorSchoolSql = $"""
            UPDATE "WorksheetComments" AS c
            SET "AuthorSchoolId" = {LiveSchool("c.\"AuthorUserId\"")}
            WHERE c."AuthorRole" = 'Student'
              AND c."AuthorSchoolId" IS NULL
              AND c."ResponsibleTeacherSource" IN ('Owner', 'CopyOwner')
              AND {LiveSchool("c.\"AuthorUserId\"")} = {LiveSchool("c.\"ResponsibleTeacherUserId\"")};
            """;

        /// <summary>
        /// Sahip kaynaklı sabit, sahibin güncel okulu yorumun <c>AuthorSchoolId</c>'sine eşit değilse (biri NULL ise de eşit
        /// sayılmaz) temizlenir: satır "sorumlu öğretmen yok" durumuna geçer. Assignment kaynaklı sabitlere dokunulmaz.
        /// </summary>
        public static readonly string ClearSql = $"""
            UPDATE "WorksheetComments" AS c
            SET "ResponsibleTeacherUserId" = NULL,
                "ResponsibleTeacherSource" = NULL
            WHERE c."ResponsibleTeacherSource" IN ('Owner', 'CopyOwner')
              AND NOT COALESCE(c."AuthorSchoolId" = {LiveSchool("c.\"ResponsibleTeacherUserId\"")}, FALSE);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResponsibleTeacherSource",
                table: "WorksheetComments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.Sql(BackfillSourceSql);
            migrationBuilder.Sql(BackfillLegacyAuthorSchoolSql);
            migrationBuilder.Sql(ClearSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponsibleTeacherSource",
                table: "WorksheetComments");
        }
    }
}

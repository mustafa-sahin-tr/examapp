using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <summary>
    /// issue #277 (madde 7) — <c>WorksheetAssignments.IsPlatformWide</c>. Kolon şema tarafı EF tarafından üretildi; backfill
    /// SQL'i elle eklendi (#259 <c>BackfillGradeAssignmentSchoolIdFromCreatorTeacher</c> / #287 <c>AddTeacherAccountApprovedAt</c>
    /// ile aynı desen).
    /// <para>
    /// Öğrenci görünürlük predikatı <c>a.SchoolId == null || a.SchoolId == okul</c> (fail-open) yerine
    /// <c>a.IsPlatformWide || a.SchoolId == okul</c> olur. Davranışı korumak için bugün "okul kısıtı yok" sayılan satırlar —
    /// öğrenci hedefi yok + sınıf hedefi var + <c>SchoolId IS NULL</c> (#259 backfill'inin bilinçli olarak dokunmadığı
    /// admin/belirsiz sınıf atamaları) — <c>IsPlatformWide = true</c> olarak işaretlenir. Öğrenci hedefli satırlar false
    /// kalır (predikatta zaten StudentId eşleşmesiyle görünür).
    /// <para>
    /// Review (security L4) daraltması — fail-closed: (1) silinmiş satırlar işaretlenmez (yeniden açılırlarsa okul kısıtsız
    /// olarak DEĞİL kapalı döner); (2) oluşturanı bir öğretmen olan satırlar (herhangi bir <c>Teachers</c> satırı, silinmiş
    /// dahil — ör. #259 backfill'inin belirsiz bıraktığı çift satırlı ya da okulsuz öğretmen ataması) işaretlenmez: platform
    /// geneli atamayı yalnızca admin yapar; admin'in exam DB'de Teachers satırı yoktur (CreateUserId'si null/0 olan legacy
    /// satırlar da admin sayılır). Bu satırlar migration sonrası yalnızca kendi okul koşuluyla (SchoolId null → hiç) görünür.
    /// </para>
    /// İdempotent: yalnızca false satırları günceller. Down kolonu düşürür (eski predikat null'a bakıyordu).
    /// </para>
    /// </summary>
    public partial class AddWorksheetAssignmentIsPlatformWide : Migration
    {
        internal const string BackfillSql = """
                UPDATE "WorksheetAssignments" AS a
                SET "IsPlatformWide" = TRUE
                WHERE a."IsPlatformWide" = FALSE
                  AND NOT a."IsDeleted"
                  AND a."SchoolId" IS NULL
                  AND a."StudentId" IS NULL
                  AND a."GradeId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM "Teachers" t WHERE t."UserId" = a."CreateUserId");
                """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPlatformWide",
                table: "WorksheetAssignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPlatformWide",
                table: "WorksheetAssignments");
        }
    }
}

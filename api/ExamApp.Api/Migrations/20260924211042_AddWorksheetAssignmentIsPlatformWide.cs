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
    /// kalır (predikatta zaten StudentId eşleşmesiyle görünür). Silinmiş satırlar da kapsamda (yeniden açılırlarsa tutarlı
    /// kalsın). İdempotent: yalnızca false satırları günceller. Down kolonu düşürür (eski predikat null'a bakıyordu).
    /// </para>
    /// </summary>
    public partial class AddWorksheetAssignmentIsPlatformWide : Migration
    {
        internal const string BackfillSql = """
                UPDATE "WorksheetAssignments"
                SET "IsPlatformWide" = TRUE
                WHERE "IsPlatformWide" = FALSE
                  AND "SchoolId" IS NULL
                  AND "StudentId" IS NULL
                  AND "GradeId" IS NOT NULL;
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

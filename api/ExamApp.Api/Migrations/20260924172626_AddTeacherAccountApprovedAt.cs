using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <summary>
    /// issue #287 — öğretmen HESAP onayı (<c>Teachers.AccountApprovedAt</c>) ile mevcut başvuru durumu
    /// (<c>ApprovalStatus</c>) ayrılır. Kolon şema tarafı EF tarafından üretildi; backfill SQL'i elle eklendi
    /// (#259 <c>BackfillGradeAssignmentSchoolIdFromCreatorTeacher</c> ile aynı desen).
    /// <para>
    /// Backfill — hesabı bugün fiilen onaylı sayılan öğretmenler kesinti yaşamasın (owner kararı 3):
    /// <list type="bullet">
    /// <item><c>ApprovalStatus = Approved</c> (1): bugün onaylı her öğretmen.</item>
    /// <item><c>SchoolId IS NOT NULL</c>: okul bağı admin onayıyla (#234) ya da #234 öncesi onaylı kayıtla kurulmuş öğretmen.
    /// Böyle bir satırın Pending/Rejected olması yalnızca "onaylı okul öğretmeni bağımsız tutor başvurusu yaptı"
    /// geçişinden gelir (#92; okul talebi yalnızca SchoolId null iken açılabilir) — bu öğretmen #287 öncesi öğretmen
    /// özelliklerini kullanıyordu ve bekleyen tutor başvurusu yüzünden erişimini kaybetmemeli.</item>
    /// </list>
    /// Diğer Pending/Rejected satırlar (ilk başvurusu bekleyen/reddedilen) null kalır → onaylanana kadar öğretmen
    /// özellikleri kapalı. Değer <c>CreateTime</c>: gerçek onay anı bilinmiyor; alan yalnızca null/dolu olarak okunur.
    /// Soft-delete edilmiş satırlar da kapsamda (yeniden açılırlarsa tutarlı kalsın). İdempotent: yalnızca null satırlar.
    /// </para>
    /// </summary>
    public partial class AddTeacherAccountApprovedAt : Migration
    {
        internal const string BackfillSql = """
                UPDATE "Teachers"
                SET "AccountApprovedAt" = "CreateTime"
                WHERE "AccountApprovedAt" IS NULL
                  AND ("ApprovalStatus" = 1 OR "SchoolId" IS NOT NULL);
                """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AccountApprovedAt",
                table: "Teachers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountApprovedAt",
                table: "Teachers");
        }
    }
}

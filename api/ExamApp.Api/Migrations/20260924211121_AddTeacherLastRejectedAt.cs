using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <summary>
    /// issue #277 (madde 2) — <c>Teachers.LastRejectedAt</c>: reddedilen öğretmenin yeni okul talebi için bekleme süresi
    /// (<c>TeacherApprovals:SchoolRequestCooldownHours</c>) bu andan sayılır. Kolon şema tarafı EF tarafından üretildi;
    /// backfill SQL'i elle eklendi (#287 <c>AddTeacherAccountApprovedAt</c> ile aynı desen).
    /// <para>
    /// Backfill: bugün Rejected (<c>ApprovalStatus = 2</c>) olan satırlar için ret anı = en son başarılı
    /// <c>TeacherRejected</c> admin audit'i (#157, <c>AdminUserActionLogs</c>; enum'lar string saklanır); audit yoksa
    /// <c>UpdateTime</c>, o da yoksa <c>CreateTime</c>. Pratikte yalnızca son 24 saatte reddedilenler bekleme süresine takılır.
    /// İdempotent: yalnızca null satırlar.
    /// </para>
    /// </summary>
    public partial class AddTeacherLastRejectedAt : Migration
    {
        internal const string BackfillSql = """
                UPDATE "Teachers" AS t
                SET "LastRejectedAt" = COALESCE(
                    (SELECT max(l."OccurredAtUtc")
                     FROM "AdminUserActionLogs" l
                     WHERE l."TargetType" = 'Teacher'
                       AND l."TargetId" = t."Id"
                       AND l."Action" = 'TeacherRejected'
                       AND l."Outcome" = 'Succeeded'),
                    t."UpdateTime",
                    t."CreateTime")
                WHERE t."ApprovalStatus" = 2
                  AND t."LastRejectedAt" IS NULL;
                """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastRejectedAt",
                table: "Teachers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRejectedAt",
                table: "Teachers");
        }
    }
}

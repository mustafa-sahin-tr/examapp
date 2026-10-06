using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <summary>
    /// issue #361: <c>Students</c> okul üyeliği doğrulaması — <c>SchoolVerifiedAt</c>/<c>SchoolVerifiedByUserId</c>, ret izi
    /// (<c>LastRejectedSchoolId</c>/<c>SchoolRejectedAt</c>/<c>SchoolRejectedByUserId</c>, 7 günlük aynı okul bekleme süresi) ve
    /// <c>CK_Students_SchoolVerifiedRequiresSchool</c> (doğrulanmış üyelik okulsuz olamaz). AddColumn/AddCheckConstraint EF
    /// tarafından üretildi; veri adımı (<see cref="BackfillExistingMembershipsSql"/>) elle eklendi (ef-migration skill istisnası,
    /// #259/#326 veri migration'larıyla aynı desen). Geçiş kararı (PO, Tur 5): migration anında okulu olan MEVCUT öğrenciler
    /// doğrulanmış sayılır (<c>SchoolVerifiedAt</c> = migration zamanı, doğrulayan NULL = sistem); sonrasında kendi kaydında okul
    /// seçen öğrenci beklemede (NULL) başlar. Adım idempotent (yalnız NULL satırlara yazar); soft-delete edilmiş satırlar da
    /// kapsanır (geri alınan silmede beklemeye düşmesin). Constraint'ten ÖNCE çalışır (yalnız okullu satırlara yazdığı için
    /// kısıtı ihlal etmez). <c>Down()</c>: constraint ve kolonlar düşer, bu migration'ın verisi kaybolur.
    /// </summary>
    public partial class AddStudentSchoolVerification : Migration
    {
        /// <summary>Mevcut okullu öğrenci üyeliklerini doğrulanmış sayar (issue #361 geçiş kararı).</summary>
        private const string BackfillExistingMembershipsSql = """
            UPDATE "Students"
            SET "SchoolVerifiedAt" = now()
            WHERE "SchoolId" IS NOT NULL AND "SchoolVerifiedAt" IS NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastRejectedSchoolId",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SchoolRejectedAt",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchoolRejectedByUserId",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SchoolVerifiedAt",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SchoolVerifiedByUserId",
                table: "Students",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(BackfillExistingMembershipsSql);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Students_SchoolVerifiedRequiresSchool",
                table: "Students",
                sql: "\"SchoolId\" IS NOT NULL OR \"SchoolVerifiedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Students_SchoolVerifiedRequiresSchool",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "LastRejectedSchoolId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "SchoolRejectedAt",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "SchoolRejectedByUserId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "SchoolVerifiedAt",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "SchoolVerifiedByUserId",
                table: "Students");
        }
    }
}

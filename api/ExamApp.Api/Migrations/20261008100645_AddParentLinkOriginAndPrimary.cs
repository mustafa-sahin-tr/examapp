using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddParentLinkOriginAndPrimary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                table: "ParentStudentLinks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "ParentStudentLinks",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CreatedByParentId",
                table: "ParentInviteCodes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParentStudentLinks_Primary_Active",
                table: "ParentStudentLinks",
                column: "StudentId",
                unique: true,
                filter: "\"IsPrimary\" AND \"Status\" = 'Active'");

            // issue #436 veri adımı (elle eklendi; CHECK kısıtından ÖNCE olmalı — ADD CONSTRAINT mevcut satırları doğrular):
            // 1) #436 öncesi tüm bağlantılar LegacyV1 (model varsayılanı yok; Unknown/boş CHECK ile yasak).
            // 2) Öğrenci başına velisi silinmemiş en eski Active satır birincil — sıra COALESCE(ActivatedAt, CreatedAt), Id
            //    (ParentLinkPrimary.PickPrimary ile aynı).
            // 3) #419 kuralına göre zaten süresi dolmuş (7 günden eski) Pending → Revoked; daha yenileri 30 günlük geçiş için kalır.
            migrationBuilder.Sql("""
                UPDATE "ParentStudentLinks" SET "Origin" = 'LegacyV1';

                UPDATE "ParentStudentLinks" l SET "IsPrimary" = TRUE
                FROM (
                    SELECT DISTINCT ON (x."StudentId") x."Id"
                    FROM "ParentStudentLinks" x
                    JOIN "Parents" p ON p."Id" = x."ParentId"
                    WHERE x."Status" = 'Active' AND NOT p."IsDeleted"
                    ORDER BY x."StudentId", COALESCE(x."ActivatedAt", x."CreatedAt"), x."Id"
                ) firsts
                WHERE l."Id" = firsts."Id";

                UPDATE "ParentStudentLinks" SET "Status" = 'Revoked', "RevokedAt" = now()
                WHERE "Status" = 'Pending' AND "CreatedAt" <= now() - interval '7 days';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ParentStudentLinks_Origin_Known",
                table: "ParentStudentLinks",
                sql: "\"Origin\" IN ('LegacyV1', 'InviteCode', 'ParentCreated', 'StudentRegistered')");

            migrationBuilder.CreateIndex(
                name: "IX_ParentInviteCodes_CreatedByParentId",
                table: "ParentInviteCodes",
                column: "CreatedByParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ParentInviteCodes_Parents_CreatedByParentId",
                table: "ParentInviteCodes",
                column: "CreatedByParentId",
                principalTable: "Parents",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ParentInviteCodes_Parents_CreatedByParentId",
                table: "ParentInviteCodes");

            migrationBuilder.DropIndex(
                name: "IX_ParentStudentLinks_Primary_Active",
                table: "ParentStudentLinks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ParentStudentLinks_Origin_Known",
                table: "ParentStudentLinks");

            migrationBuilder.DropIndex(
                name: "IX_ParentInviteCodes_CreatedByParentId",
                table: "ParentInviteCodes");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                table: "ParentStudentLinks");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "ParentStudentLinks");

            migrationBuilder.DropColumn(
                name: "CreatedByParentId",
                table: "ParentInviteCodes");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddBadgeDefinitionCodeAndAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Code starts nullable so we can backfill existing (pre-#148) seeder rows below, then gets
            // tightened to NOT NULL once every row has a value.
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "BadgeDefinitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "BadgeDefinitions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "BadgeDefinitions",
                type: "text",
                nullable: true);

            // Issue #148 owner decision #3/#4: pre-existing badges were already being evaluated for
            // everyone, so the migration must not silently disable them — default IsActive to true for
            // both the backfill and any future row that doesn't set it explicitly.
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "BadgeDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "BadgeDefinitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "BadgeDefinitions",
                type: "text",
                nullable: true);

            // Backfill Code for rows created by BadgeSeeder before this issue (#148) existed, matched by
            // the Name it seeded them with (see BadgeSeeder.SeedAsync for the same Code assignments used
            // going forward). Anything not matched below (should not happen — no admin-CRUD existed yet,
            // so every row was seeder-created) gets a generated fallback Code so the NOT NULL/unique
            // constraints added below can never fail the migration outright; such a row would need a
            // manual look before trusting it, so it's tagged obviously ("legacy-<id>") rather than guessed.
            migrationBuilder.Sql(@"
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'first-answer' WHERE ""Name"" = 'İlk Cevap';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'correct-streak-5' WHERE ""Name"" = '5 Doğru Üst Üste';

                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'question-hunter-1' WHERE ""Name"" = 'Soru Avcısı I';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'question-hunter-2' WHERE ""Name"" = 'Soru Avcısı II';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'question-hunter-3' WHERE ""Name"" = 'Soru Avcısı III';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'question-hunter-4' WHERE ""Name"" = 'Soru Avcısı IV';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'question-hunter-5' WHERE ""Name"" = 'Soru Avcısı V';

                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'accuracy-journey-1' WHERE ""Name"" = 'Doğru Yolu Bul I';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'accuracy-journey-2' WHERE ""Name"" = 'Doğru Yolu Bul II';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'accuracy-journey-3' WHERE ""Name"" = 'Doğru Yolu Bul III';

                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'study-time-1' WHERE ""Name"" = 'Hızlı Başlangıç';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'study-time-2' WHERE ""Name"" = 'Show Time';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'study-time-3' WHERE ""Name"" = 'Bilgi Avcısı';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'study-time-4' WHERE ""Name"" = 'Prime Time';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'study-time-5' WHERE ""Name"" = 'Bilge İzleyici';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'study-time-6' WHERE ""Name"" = 'Zaman Yolcusu';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'study-time-7' WHERE ""Name"" = 'Akademik Yolculuk';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'study-time-8' WHERE ""Name"" = 'Elit Çalışkan';

                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-turkce-mastery' WHERE ""Name"" = 'Türkçe Ustası';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-turkce-expert' WHERE ""Name"" = 'Türkçe Uzmanı';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-turkce-time' WHERE ""Name"" = 'Türkçe Zaman Ustası';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-matematik-mastery' WHERE ""Name"" = 'Matematik Ustası';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-matematik-expert' WHERE ""Name"" = 'Matematik Uzmanı';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-matematik-time' WHERE ""Name"" = 'Matematik Zaman Ustası';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-fen-bilimleri-mastery' WHERE ""Name"" = 'Fen Bilimleri Ustası';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-fen-bilimleri-expert' WHERE ""Name"" = 'Fen Bilimleri Uzmanı';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-fen-bilimleri-time' WHERE ""Name"" = 'Fen Bilimleri Zaman Ustası';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-sosyal-bilgiler-mastery' WHERE ""Name"" = 'Sosyal Bilgiler Ustası';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-sosyal-bilgiler-expert' WHERE ""Name"" = 'Sosyal Bilgiler Uzmanı';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'subject-sosyal-bilgiler-time' WHERE ""Name"" = 'Sosyal Bilgiler Zaman Ustası';

                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'streak-1' WHERE ""Name"" = 'İstikrarlı Öğrenci I';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'streak-2' WHERE ""Name"" = 'İstikrarlı Öğrenci II';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'streak-3' WHERE ""Name"" = 'İstikrarlı Öğrenci III';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'streak-4' WHERE ""Name"" = 'İstikrarlı Öğrenci IV';

                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'active-days-1' WHERE ""Name"" = 'Yeni Alışkanlıklar';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'active-days-2' WHERE ""Name"" = 'Alışkanlık Sahibi';
                UPDATE ""BadgeDefinitions"" SET ""Code"" = 'active-days-3' WHERE ""Name"" = 'Sürekli Öğrenen';

                UPDATE ""BadgeDefinitions""
                SET ""Code"" = 'legacy-' || substr(md5(random()::text || ""Id""::text), 1, 12)
                WHERE ""Code"" IS NULL;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "BadgeDefinitions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BadgeDefinitions_Code",
                table: "BadgeDefinitions",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BadgeDefinitions_Code",
                table: "BadgeDefinitions");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "BadgeDefinitions");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "BadgeDefinitions");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "BadgeDefinitions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "BadgeDefinitions");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "BadgeDefinitions");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "BadgeDefinitions");
        }
    }
}

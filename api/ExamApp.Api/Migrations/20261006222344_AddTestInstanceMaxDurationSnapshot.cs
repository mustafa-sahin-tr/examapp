using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTestInstanceMaxDurationSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxDurationSeconds",
                table: "TestInstances",
                type: "integer",
                nullable: true);

            // issue #396 (elle eklendi, ef-migration skill istisnası): mevcut instance'ların süre sınırı kopyası worksheet'in
            // bugünkü değerinden doldurulur — açık oturumlar sunucu süre kontrolüne girer. Silinmiş worksheet'ler de dahil.
            migrationBuilder.Sql(
                """
                UPDATE "TestInstances" AS ti
                SET "MaxDurationSeconds" = w."MaxDurationSeconds"
                FROM "Worksheets" AS w
                WHERE w."Id" = ti."WorksheetId" AND ti."MaxDurationSeconds" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TestInstances_StartTime_Started",
                table: "TestInstances",
                column: "StartTime",
                filter: "\"Status\" = 0 AND NOT \"IsDeleted\" AND \"MaxDurationSeconds\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestInstances_StartTime_Started",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "MaxDurationSeconds",
                table: "TestInstances");
        }
    }
}

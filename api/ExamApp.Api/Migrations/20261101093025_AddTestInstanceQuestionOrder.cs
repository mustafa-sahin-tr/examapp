using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTestInstanceQuestionOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "TestInstanceQuestions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // issue #685: mevcut oturumlar o anki görüntü sırasıyla doldurulur — eski okuma kuralı (WorksheetQuestion.Order, Id);
            // silinmiş satırlar ve kaldırılmış maddeler (#684) dahil, böylece hiçbir oturumun sırası değişmez.
            migrationBuilder.Sql("""
                UPDATE "TestInstanceQuestions" AS t
                SET "Order" = r.rn
                FROM (
                    SELECT tiq."Id",
                           ROW_NUMBER() OVER (PARTITION BY tiq."WorksheetInstanceId" ORDER BY wq."Order", wq."Id", tiq."Id") AS rn
                    FROM "TestInstanceQuestions" tiq
                    JOIN "TestQuestions" wq ON wq."Id" = tiq."WorksheetQuestionId"
                ) AS r
                WHERE t."Id" = r."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Order",
                table: "TestInstanceQuestions");
        }
    }
}

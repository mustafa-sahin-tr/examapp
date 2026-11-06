using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class UniqueBadgeEarnedPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tekil index'ten önce eski çift kazanımları temizle: (kullanıcı, rozet) başına en eski satır kalır.
            migrationBuilder.Sql("""
                DELETE FROM "BadgeEarned" e
                USING (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "UserId", "BadgeDefinitionId" ORDER BY "EarnedDate", "Id") AS rn
                    FROM "BadgeEarned"
                ) d
                WHERE e."Id" = d."Id" AND d.rn > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BadgeEarned_UserId_BadgeDefinitionId",
                table: "BadgeEarned",
                columns: new[] { "UserId", "BadgeDefinitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BadgeEarned_UserId_BadgeDefinitionId",
                table: "BadgeEarned");
        }
    }
}

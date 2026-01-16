using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApi.Migrations
{
    /// <inheritdoc />
    public partial class AddFundTaxRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FundTaxRates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AssetId = table.Column<string>(type: "text", nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundTaxRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FundTaxRates_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "AllowedCryptos",
                keyColumn: "Id",
                keyValue: "crypto-btc",
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "AllowedCryptos",
                keyColumn: "Id",
                keyValue: "crypto-eth",
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "1",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3395));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "10",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3426));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "15",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3443));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "16",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3448));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "17",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3464));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "18",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3484));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "2",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3410));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "3",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3415));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "4",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3433));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "5",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3438));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "6",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3470));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "7",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3474));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "8",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3480));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "9",
                column: "LastUpdated",
                value: new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3421));

            migrationBuilder.CreateIndex(
                name: "IX_FundTaxRates_AssetId",
                table: "FundTaxRates",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_FundTaxRates_UserId_AssetId",
                table: "FundTaxRates",
                columns: new[] { "UserId", "AssetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FundTaxRates");

            migrationBuilder.UpdateData(
                table: "AllowedCryptos",
                keyColumn: "Id",
                keyValue: "crypto-btc",
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9230), new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9240) });

            migrationBuilder.UpdateData(
                table: "AllowedCryptos",
                keyColumn: "Id",
                keyValue: "crypto-eth",
                columns: new[] { "CreatedAt", "UpdatedAt" },
                values: new object[] { new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9240), new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9250) });

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "1",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9310));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "10",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9350));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "15",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9370));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "16",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9370));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "17",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9380));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "18",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9430));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "2",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9330));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "3",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9340));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "4",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9360));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "5",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9360));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "6",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9410));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "7",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9410));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "8",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9420));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "9",
                column: "LastUpdated",
                value: new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9340));
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinanceApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowedCryptos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllowedCryptos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CoinGeckoId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    YahooSymbol = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowedCryptos", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AllowedCryptos",
                columns: new[] { "Id", "CoinGeckoId", "CreatedAt", "IsEnabled", "Name", "Symbol", "UpdatedAt", "YahooSymbol" },
                values: new object[,]
                {
                    { "crypto-btc", "bitcoin", new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9230), true, "Bitcoin", "BTC", new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9240), "BTC-USD" },
                    { "crypto-eth", "ethereum", new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9240), true, "Ethereum", "ETH", new DateTime(2025, 12, 23, 7, 56, 54, 74, DateTimeKind.Utc).AddTicks(9250), "ETH-USD" }
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_AllowedCryptos_Symbol",
                table: "AllowedCryptos",
                column: "Symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllowedCryptos");

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "1",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7504));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "10",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7540));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "15",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7559));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "16",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7565));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "17",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7571));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "18",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7676));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "2",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7521));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "3",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7527));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "4",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7547));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "5",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7553));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "6",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7578));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "7",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7583));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "8",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7671));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "9",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 9, 46, 1, 622, DateTimeKind.Utc).AddTicks(7533));
        }
    }
}

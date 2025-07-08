using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApi.Migrations
{
    /// <inheritdoc />
    public partial class AddProfitLossHistory2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FromCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangePercentage = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ChangeValue = table.Column<decimal>(type: "numeric(18,8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserCurrencyPreferences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PreferredCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCurrencyPreferences", x => x.Id);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_FromCurrency_ToCurrency",
                table: "ExchangeRates",
                columns: new[] { "FromCurrency", "ToCurrency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCurrencyPreferences_UserId",
                table: "UserCurrencyPreferences",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "UserCurrencyPreferences");

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "1",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(5895));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "10",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(5999));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "15",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(6020));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "16",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(6025));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "17",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(6031));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "18",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(6052));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "2",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(5956));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "3",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(5987));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "4",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(6007));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "5",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(6013));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "6",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(6037));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "7",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(6042));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "8",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(6047));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "9",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 8, 8, 31, 55, 9, DateTimeKind.Utc).AddTicks(5993));
        }
    }
}

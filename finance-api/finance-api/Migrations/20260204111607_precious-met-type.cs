using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApi.Migrations
{
    /// <inheritdoc />
    public partial class preciousmettype : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "1",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1704));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "10",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1744));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "15",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1764));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "16",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1770));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "17",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1778));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "18",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1827));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "2",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1722));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "3",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1730));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "4",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1752));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "5",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1758));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "6",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1783));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "7",
                columns: new[] { "LastUpdated", "Type" },
                values: new object[] { new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1790), 2 });

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "8",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1820));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "9",
                column: "LastUpdated",
                value: new DateTime(2026, 2, 4, 11, 16, 6, 570, DateTimeKind.Utc).AddTicks(1736));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                columns: new[] { "LastUpdated", "Type" },
                values: new object[] { new DateTime(2026, 1, 16, 13, 10, 14, 748, DateTimeKind.Utc).AddTicks(3474), 3 });

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
        }
    }
}

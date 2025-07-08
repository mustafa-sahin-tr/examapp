using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceApi.Migrations
{
    /// <inheritdoc />
    public partial class AddProfitLossHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfitLossHistories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalProfitLoss = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TotalInvestment = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    TotalCurrentValue = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ProfitLossPercentage = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AssetTypeBreakdown = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Hour = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfitLossHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetProfitLosses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProfitLossHistoryId = table.Column<string>(type: "text", nullable: false),
                    AssetId = table.Column<string>(type: "text", nullable: false),
                    ProfitLoss = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Investment = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CurrentValue = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ProfitLossPercentage = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    AveragePrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetProfitLosses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetProfitLosses_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetProfitLosses_ProfitLossHistories_ProfitLossHistoryId",
                        column: x => x.ProfitLossHistoryId,
                        principalTable: "ProfitLossHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetTypeProfitLosses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProfitLossHistoryId = table.Column<string>(type: "text", nullable: false),
                    AssetType = table.Column<int>(type: "integer", nullable: false),
                    ProfitLoss = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Investment = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    CurrentValue = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ProfitLossPercentage = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    AssetCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTypeProfitLosses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetTypeProfitLosses_ProfitLossHistories_ProfitLossHistory~",
                        column: x => x.ProfitLossHistoryId,
                        principalTable: "ProfitLossHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_AssetProfitLosses_AssetId",
                table: "AssetProfitLosses",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetProfitLosses_ProfitLossHistoryId_AssetId",
                table: "AssetProfitLosses",
                columns: new[] { "ProfitLossHistoryId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypeProfitLosses_ProfitLossHistoryId_AssetType",
                table: "AssetTypeProfitLosses",
                columns: new[] { "ProfitLossHistoryId", "AssetType" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfitLossHistories_UserId_Date_Hour",
                table: "ProfitLossHistories",
                columns: new[] { "UserId", "Date", "Hour" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetProfitLosses");

            migrationBuilder.DropTable(
                name: "AssetTypeProfitLosses");

            migrationBuilder.DropTable(
                name: "ProfitLossHistories");

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "1",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6606));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "10",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6642));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "15",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6659));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "16",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6663));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "17",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6670));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "18",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6690));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "2",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6625));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "3",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6630));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "4",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6649));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "5",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6653));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "6",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6674));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "7",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6680));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "8",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6685));

            migrationBuilder.UpdateData(
                table: "Assets",
                keyColumn: "Id",
                keyValue: "9",
                column: "LastUpdated",
                value: new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6636));
        }
    }
}

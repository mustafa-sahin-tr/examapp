using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinanceApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    CurrentPrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangePercentage = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    ChangeValue = table.Column<decimal>(type: "numeric(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Portfolios",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AssetId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    TotalQuantity = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    AveragePrice = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Portfolios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Portfolios_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AssetId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Fees = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PortfolioId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Transactions_Portfolios_PortfolioId",
                        column: x => x.PortfolioId,
                        principalTable: "Portfolios",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Assets",
                columns: new[] { "Id", "ChangePercentage", "ChangeValue", "Currency", "CurrentPrice", "LastUpdated", "Name", "Symbol", "Type" },
                values: new object[,]
                {
                    { "1", 2.5m, 2.08m, "TRY", 85.50m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6606), "Tüpraş", "TUPRS", 0 },
                    { "10", -0.8m, -0.26m, "TRY", 31.85m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6642), "Ereğli Demir Çelik", "EREGL", 0 },
                    { "15", 2.3m, 3.21m, "USD", 142.65m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6659), "Alphabet Inc.", "GOOGL", 1 },
                    { "16", 1.1m, 1.65m, "USD", 151.94m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6663), "Amazon.com Inc.", "AMZN", 1 },
                    { "17", -2.1m, -5.32m, "USD", 248.50m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6670), "Tesla Inc.", "TSLA", 1 },
                    { "18", 0.3m, 0.000259m, "TRY", 0.086543m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6690), "Garanti Portföy Özel Sektör Borçlanma Araçları Fonu", "GAR001", 4 },
                    { "2", -1.2m, -0.51m, "TRY", 42.30m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6625), "Akbank", "AKBNK", 0 },
                    { "3", 3.1m, 4.71m, "TRY", 156.80m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6630), "Türk Hava Yolları", "THYAO", 0 },
                    { "4", 1.8m, 3.42m, "USD", 192.45m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6649), "Apple Inc.", "AAPL", 1 },
                    { "5", -0.5m, -1.89m, "USD", 378.20m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6653), "Microsoft Corporation", "MSFT", 1 },
                    { "6", 0.8m, 15.92m, "USD", 2012.45m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6674), "Gold", "GOLD", 2 },
                    { "7", -1.5m, -0.38m, "USD", 24.85m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6680), "Silver", "SILVER", 3 },
                    { "8", 1.2m, 0.001493m, "TRY", 0.125864m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6685), "QNB Finans Portföy A.Ş. Hisse Senedi Fonu", "QNB001", 4 },
                    { "9", 1.8m, 2.22m, "TRY", 125.40m, new DateTime(2025, 7, 2, 19, 44, 52, 956, DateTimeKind.Utc).AddTicks(6636), "Aselsan", "ASELS", 0 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Symbol_Type",
                table: "Assets",
                columns: new[] { "Symbol", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Portfolios_AssetId",
                table: "Portfolios",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Portfolios_UserId_AssetId",
                table: "Portfolios",
                columns: new[] { "UserId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AssetId",
                table: "Transactions",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PortfolioId",
                table: "Transactions",
                column: "PortfolioId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_AssetId_Date",
                table: "Transactions",
                columns: new[] { "UserId", "AssetId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Portfolios");

            migrationBuilder.DropTable(
                name: "Assets");
        }
    }
}

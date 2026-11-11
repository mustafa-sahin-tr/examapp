using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanPriceChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PriceChangeNoticeForUtc",
                table: "UserSubscriptionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PendingMonthlyPrice",
                table: "SubscriptionPlans",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingMonthlyPriceFromUtc",
                table: "SubscriptionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingFromUtc",
                table: "SubscriptionPlanPrices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PendingPrice",
                table: "SubscriptionPlanPrices",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 19,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 20,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 21,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 22,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 23,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 24,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 25,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 26,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 27,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 28,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 29,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 30,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 31,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 32,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 33,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 34,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 35,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 36,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 37,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 38,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 39,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 40,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 41,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 42,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 43,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 44,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 45,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 46,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 47,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 48,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 49,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 50,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 51,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 52,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 53,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 54,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 55,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 56,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 57,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 58,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 59,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 60,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 61,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 62,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 63,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 64,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 65,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 66,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 67,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 68,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 69,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 70,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 71,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 72,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 73,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 74,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 75,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 76,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 77,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 78,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 79,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 80,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 81,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 82,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 83,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 84,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 85,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 86,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 87,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 88,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 89,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 90,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 91,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 92,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 93,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 94,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 95,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 96,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 97,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 98,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 99,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlanPrices",
                keyColumn: "Id",
                keyValue: 100,
                columns: new[] { "PendingFromUtc", "PendingPrice" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PendingMonthlyPrice", "PendingMonthlyPriceFromUtc" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "PendingMonthlyPrice", "PendingMonthlyPriceFromUtc" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "PendingMonthlyPrice", "PendingMonthlyPriceFromUtc" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "PendingMonthlyPrice", "PendingMonthlyPriceFromUtc" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "PendingMonthlyPrice", "PendingMonthlyPriceFromUtc" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "PendingMonthlyPrice", "PendingMonthlyPriceFromUtc" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "PendingMonthlyPrice", "PendingMonthlyPriceFromUtc" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "PendingMonthlyPrice", "PendingMonthlyPriceFromUtc" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "PendingMonthlyPrice", "PendingMonthlyPriceFromUtc" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceChangeNoticeForUtc",
                table: "UserSubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "PendingMonthlyPrice",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "PendingMonthlyPriceFromUtc",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "PendingFromUtc",
                table: "SubscriptionPlanPrices");

            migrationBuilder.DropColumn(
                name: "PendingPrice",
                table: "SubscriptionPlanPrices");
        }
    }
}

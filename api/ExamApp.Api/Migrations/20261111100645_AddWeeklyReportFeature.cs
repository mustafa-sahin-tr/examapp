using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyReportFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SubscriptionPlanFeatureLimits",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "FeatureKey", "IsDeleted", "MonthlyLimit", "PlanId", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 88, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "weekly-report", false, null, 8, null, null },
                    { 89, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "weekly-report", false, null, 9, null, null },
                    { 90, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "weekly-report", false, null, 2, null, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 88);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 89);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 90);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQuickAdd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "BookId",
                table: "QuestionSources",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "BookImportJobs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.InsertData(
                table: "SubscriptionPlanFeatureLimits",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "FeatureKey", "IsDeleted", "MonthlyLimit", "PlanId", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 10, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "quick-add", false, 5, 1, null, null },
                    { 11, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "quick-add", false, 20, 2, null, null },
                    { 12, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "quick-add", false, 200, 3, null, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "BookImportJobs");

            migrationBuilder.AlterColumn<int>(
                name: "BookId",
                table: "QuestionSources",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}

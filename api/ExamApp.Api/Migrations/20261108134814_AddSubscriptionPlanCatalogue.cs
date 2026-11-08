using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionPlanCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "SubscriptionPlans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Any");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "SubscriptionPlans",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "TRY");

            migrationBuilder.AddColumn<string>(
                name: "DisplayNameKey",
                table: "SubscriptionPlans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyPrice",
                table: "SubscriptionPlans",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "SubscriptionPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TrialDays",
                table: "SubscriptionPlans",
                type: "integer",
                nullable: true);

            migrationBuilder.InsertData(
                table: "SubscriptionPlanFeatureLimits",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "FeatureKey", "IsDeleted", "MonthlyLimit", "PlanId", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 2, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "ai-classification", false, 20, 1, null, null },
                    { 3, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "mock-exam", false, 0, 1, null, null }
                });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Audience", "Currency", "DisplayNameKey", "MonthlyPrice", "SortOrder", "TrialDays" },
                values: new object[] { "Any", "TRY", "subscription.plans.free", 0m, 0, null });

            migrationBuilder.InsertData(
                table: "SubscriptionPlans",
                columns: new[] { "Id", "Audience", "CreateTime", "CreateUserId", "Currency", "DeleteTime", "DeleteUserId", "DisplayNameKey", "IsActive", "IsDeleted", "Key", "MonthlyPrice", "Name", "SortOrder", "TrialDays", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 2, "Any", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, "TRY", null, null, "subscription.plans.trial", true, false, "trial", 0m, "Trial", 1, 14, null, null },
                    { 3, "Teacher", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, "TRY", null, null, "subscription.plans.teacherPro", true, false, "teacher-pro", 199m, "Teacher Pro", 2, null, null, null },
                    { 4, "Student", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, "TRY", null, null, "subscription.plans.studentPlus", true, false, "student-plus", 99m, "Student Plus", 3, null, null, null }
                });

            migrationBuilder.InsertData(
                table: "SubscriptionPlanFeatureLimits",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "FeatureKey", "IsDeleted", "MonthlyLimit", "PlanId", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 4, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "book-import", false, 1, 2, null, null },
                    { 5, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "ai-classification", false, 50, 2, null, null },
                    { 6, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "mock-exam", false, 2, 2, null, null },
                    { 7, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "book-import", false, 10, 3, null, null },
                    { 8, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "ai-classification", false, 1000, 3, null, null },
                    { 9, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "mock-exam", false, null, 4, null, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "DisplayNameKey",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "MonthlyPrice",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "TrialDays",
                table: "SubscriptionPlans");
        }
    }
}

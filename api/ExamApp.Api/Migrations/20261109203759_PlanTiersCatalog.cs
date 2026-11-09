using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class PlanTiersCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.InsertData(
                table: "SubscriptionPlans",
                columns: new[] { "Id", "Audience", "CreateTime", "CreateUserId", "Currency", "DeleteTime", "DeleteUserId", "DisplayNameKey", "IsActive", "IsDeleted", "Key", "MonthlyPrice", "Name", "SortOrder", "TrialDays", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 5, "Teacher", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, "TRY", null, null, "subscription.plans.teacherBasic", true, false, "teacher-basic", 149m, "Teacher Basic", 2, null, null, null },
                    { 6, "Teacher", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, "TRY", null, null, "subscription.plans.teacherEssential", true, false, "teacher-essential", 299m, "Teacher Essential", 3, null, null, null },
                    { 7, "Student", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, "TRY", null, null, "subscription.plans.studentBasic", true, false, "student-basic", 99m, "Student Basic", 5, null, null, null },
                    { 8, "Student", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, "TRY", null, null, "subscription.plans.studentEssential", true, false, "student-essential", 199m, "Student Essential", 6, null, null, null },
                    { 9, "Student", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, "TRY", null, null, "subscription.plans.studentPro", true, false, "student-pro", 349m, "Student Pro", 7, null, null, null }
                });

            migrationBuilder.InsertData(
                table: "SubscriptionPlanFeatureLimits",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "FeatureKey", "IsDeleted", "MonthlyLimit", "PlanId", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 26, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "manual-labeling", false, null, 3, null, null },
                    { 27, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "auto-detect", false, null, 3, null, null },
                    { 28, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "canvas-ai", false, 1000, 3, null, null },
                    { 29, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "question-generation", false, 200, 3, null, null },
                    { 30, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "activity-editor", false, null, 3, null, null },
                    { 31, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "flashcard-sets", false, null, 3, null, null },
                    { 32, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "coach-panel", false, null, 3, null, null },
                    { 33, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "print-test", false, null, 3, null, null },
                    { 34, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "messaging", false, null, 3, null, null },
                    { 35, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "tutoring", false, null, 3, null, null },
                    { 36, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "live-lesson", false, 40, 3, null, null },
                    { 58, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "manual-labeling", false, null, 2, null, null },
                    { 59, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "auto-detect", false, null, 2, null, null },
                    { 60, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "canvas-ai", false, 30, 2, null, null },
                    { 61, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "question-generation", false, 20, 2, null, null },
                    { 62, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "activity-editor", false, null, 2, null, null },
                    { 63, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "flashcard-sets", false, null, 2, null, null },
                    { 64, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "coach-panel", false, null, 2, null, null },
                    { 65, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "print-test", false, null, 2, null, null },
                    { 66, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "messaging", false, null, 2, null, null },
                    { 67, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "tutoring", false, null, 2, null, null },
                    { 68, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "live-lesson", false, 2, 2, null, null },
                    { 69, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "mixed-mini", false, 2, 2, null, null },
                    { 70, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "practice", false, 10, 2, null, null },
                    { 71, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "self-activity", false, 10, 2, null, null },
                    { 72, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "study-tools", false, null, 2, null, null },
                    { 73, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "analytics", false, null, 2, null, null }
                });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "MonthlyPrice", "SortOrder" },
                values: new object[] { 599m, 4 });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "IsActive", "SortOrder" },
                values: new object[] { false, 8 });

            migrationBuilder.InsertData(
                table: "SubscriptionPlanFeatureLimits",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "FeatureKey", "IsDeleted", "MonthlyLimit", "PlanId", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 13, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "manual-labeling", false, null, 5, null, null },
                    { 14, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "manual-labeling", false, null, 6, null, null },
                    { 15, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "auto-detect", false, null, 6, null, null },
                    { 16, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "ai-classification", false, 500, 6, null, null },
                    { 17, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "canvas-ai", false, 300, 6, null, null },
                    { 18, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "quick-add", false, 100, 6, null, null },
                    { 19, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "activity-editor", false, null, 6, null, null },
                    { 20, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "flashcard-sets", false, null, 6, null, null },
                    { 21, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "coach-panel", false, null, 6, null, null },
                    { 22, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "print-test", false, null, 6, null, null },
                    { 23, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "messaging", false, null, 6, null, null },
                    { 24, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "tutoring", false, null, 6, null, null },
                    { 25, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "live-lesson", false, 10, 6, null, null },
                    { 37, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "mock-exam", false, 3, 7, null, null },
                    { 38, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "mixed-mini", false, 3, 7, null, null },
                    { 39, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "practice", false, 10, 7, null, null },
                    { 40, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "self-activity", false, 10, 7, null, null },
                    { 41, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "mock-exam", false, 6, 8, null, null },
                    { 42, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "mixed-mini", false, 6, 8, null, null },
                    { 43, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "practice", false, 50, 8, null, null },
                    { 44, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "self-activity", false, 50, 8, null, null },
                    { 45, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "study-tools", false, null, 8, null, null },
                    { 46, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "messaging", false, null, 8, null, null },
                    { 47, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "tutoring", false, null, 8, null, null },
                    { 48, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "live-lesson", false, null, 8, null, null },
                    { 49, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "mock-exam", false, null, 9, null, null },
                    { 50, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "mixed-mini", false, null, 9, null, null },
                    { 51, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "practice", false, null, 9, null, null },
                    { 52, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "self-activity", false, null, 9, null, null },
                    { 53, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "study-tools", false, null, 9, null, null },
                    { 54, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "analytics", false, null, 9, null, null },
                    { 55, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "messaging", false, null, 9, null, null },
                    { 56, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "tutoring", false, null, 9, null, null },
                    { 57, new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "live-lesson", false, null, 9, null, null }
                });
        

            // #1093: Student Plus pasif; mevcut atamalar Student Pro'ya geçer (aynı sınırsız deneme + yeni özellikler).
            migrationBuilder.Sql(@"UPDATE ""UserSubscriptionPlans"" SET ""PlanId"" = 9 WHERE ""PlanId"" = 4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE ""UserSubscriptionPlans"" SET ""PlanId"" = 4 WHERE ""PlanId"" = 9;");

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 22);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 27);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 28);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 29);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 30);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 31);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 32);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 33);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 34);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 35);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 36);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 37);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 38);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 39);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 40);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 41);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 42);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 43);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 44);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 45);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 46);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 47);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 48);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 49);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 50);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 51);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 52);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 53);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 55);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 56);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 57);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 58);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 59);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 60);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 61);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 62);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 63);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 64);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 65);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 66);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 67);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 68);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 69);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 70);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 71);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 72);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlanFeatureLimits",
                keyColumn: "Id",
                keyValue: 73);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "MonthlyPrice", "SortOrder" },
                values: new object[] { 199m, 2 });

            migrationBuilder.UpdateData(
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "IsActive", "SortOrder" },
                values: new object[] { true, 3 });
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class StudentPlanPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiscountPercent",
                table: "UserSubscriptionPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Duration",
                table: "UserSubscriptionPlans",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                // Mevcut atamalar aylık, bütün dersler (#1108).
                defaultValue: "Monthly");

            migrationBuilder.AddColumn<decimal>(
                name: "PricePaid",
                table: "UserSubscriptionPlans",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScopeSubjectId",
                table: "UserSubscriptionPlans",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionPlanPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'1000', '1', '', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    Band = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GradeLevel = table.Column<int>(type: "integer", nullable: true),
                    Scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Duration = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdateUserId = table.Column<int>(type: "integer", nullable: true),
                    DeleteTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeleteUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanPrices_SubscriptionPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SubscriptionPlanPrices",
                columns: new[] { "Id", "Band", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "Duration", "GradeLevel", "IsDeleted", "PlanId", "Price", "Scope", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 1, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 7, 149m, "AllSubjects", null, null },
                    { 2, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 7, 805m, "AllSubjects", null, null },
                    { 3, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 7, 1341m, "AllSubjects", null, null },
                    { 4, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 7, 75m, "AllSubjects", null, null },
                    { 5, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 7, 89m, "SingleSubject", null, null },
                    { 6, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 7, 483m, "SingleSubject", null, null },
                    { 7, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 7, 805m, "SingleSubject", null, null },
                    { 8, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 7, 45m, "SingleSubject", null, null },
                    { 9, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 8, 249m, "AllSubjects", null, null },
                    { 10, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 8, 1345m, "AllSubjects", null, null },
                    { 11, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 8, 2241m, "AllSubjects", null, null },
                    { 12, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 8, 125m, "AllSubjects", null, null },
                    { 13, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 8, 149m, "SingleSubject", null, null },
                    { 14, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 8, 807m, "SingleSubject", null, null },
                    { 15, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 8, 1345m, "SingleSubject", null, null },
                    { 16, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 8, 75m, "SingleSubject", null, null },
                    { 17, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 9, 399m, "AllSubjects", null, null },
                    { 18, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 9, 2155m, "AllSubjects", null, null },
                    { 19, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 9, 3591m, "AllSubjects", null, null },
                    { 20, "Primary", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 9, 200m, "AllSubjects", null, null },
                    { 21, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 7, 199m, "AllSubjects", null, null },
                    { 22, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 7, 1075m, "AllSubjects", null, null },
                    { 23, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 7, 1791m, "AllSubjects", null, null },
                    { 24, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 7, 100m, "AllSubjects", null, null },
                    { 25, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 7, 119m, "SingleSubject", null, null },
                    { 26, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 7, 645m, "SingleSubject", null, null },
                    { 27, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 7, 1075m, "SingleSubject", null, null },
                    { 28, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 7, 60m, "SingleSubject", null, null },
                    { 29, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 8, 349m, "AllSubjects", null, null },
                    { 30, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 8, 1885m, "AllSubjects", null, null },
                    { 31, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 8, 3141m, "AllSubjects", null, null },
                    { 32, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 8, 175m, "AllSubjects", null, null },
                    { 33, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 8, 209m, "SingleSubject", null, null },
                    { 34, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 8, 1131m, "SingleSubject", null, null },
                    { 35, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 8, 1885m, "SingleSubject", null, null },
                    { 36, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 8, 105m, "SingleSubject", null, null },
                    { 37, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 9, 549m, "AllSubjects", null, null },
                    { 38, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 9, 2965m, "AllSubjects", null, null },
                    { 39, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 9, 4941m, "AllSubjects", null, null },
                    { 40, "Middle", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 9, 275m, "AllSubjects", null, null },
                    { 41, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 7, 249m, "AllSubjects", null, null },
                    { 42, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 7, 1345m, "AllSubjects", null, null },
                    { 43, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 7, 2241m, "AllSubjects", null, null },
                    { 44, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 7, 125m, "AllSubjects", null, null },
                    { 45, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 7, 149m, "SingleSubject", null, null },
                    { 46, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 7, 807m, "SingleSubject", null, null },
                    { 47, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 7, 1345m, "SingleSubject", null, null },
                    { 48, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 7, 75m, "SingleSubject", null, null },
                    { 49, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 8, 449m, "AllSubjects", null, null },
                    { 50, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 8, 2425m, "AllSubjects", null, null },
                    { 51, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 8, 4041m, "AllSubjects", null, null },
                    { 52, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 8, 225m, "AllSubjects", null, null },
                    { 53, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 8, 269m, "SingleSubject", null, null },
                    { 54, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 8, 1455m, "SingleSubject", null, null },
                    { 55, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 8, 2425m, "SingleSubject", null, null },
                    { 56, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 8, 135m, "SingleSubject", null, null },
                    { 57, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 9, 699m, "AllSubjects", null, null },
                    { 58, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 9, 3775m, "AllSubjects", null, null },
                    { 59, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 9, 6291m, "AllSubjects", null, null },
                    { 60, "Lgs", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 9, 350m, "AllSubjects", null, null },
                    { 61, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 7, 249m, "AllSubjects", null, null },
                    { 62, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 7, 1345m, "AllSubjects", null, null },
                    { 63, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 7, 2241m, "AllSubjects", null, null },
                    { 64, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 7, 125m, "AllSubjects", null, null },
                    { 65, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 7, 149m, "SingleSubject", null, null },
                    { 66, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 7, 807m, "SingleSubject", null, null },
                    { 67, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 7, 1345m, "SingleSubject", null, null },
                    { 68, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 7, 75m, "SingleSubject", null, null },
                    { 69, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 8, 449m, "AllSubjects", null, null },
                    { 70, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 8, 2425m, "AllSubjects", null, null },
                    { 71, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 8, 4041m, "AllSubjects", null, null },
                    { 72, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 8, 225m, "AllSubjects", null, null },
                    { 73, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 8, 269m, "SingleSubject", null, null },
                    { 74, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 8, 1455m, "SingleSubject", null, null },
                    { 75, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 8, 2425m, "SingleSubject", null, null },
                    { 76, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 8, 135m, "SingleSubject", null, null },
                    { 77, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 9, 699m, "AllSubjects", null, null },
                    { 78, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 9, 3775m, "AllSubjects", null, null },
                    { 79, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 9, 6291m, "AllSubjects", null, null },
                    { 80, "HighSchool", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 9, 350m, "AllSubjects", null, null },
                    { 81, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 7, 299m, "AllSubjects", null, null },
                    { 82, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 7, 1615m, "AllSubjects", null, null },
                    { 83, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 7, 2691m, "AllSubjects", null, null },
                    { 84, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 7, 150m, "AllSubjects", null, null },
                    { 85, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 7, 179m, "SingleSubject", null, null },
                    { 86, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 7, 969m, "SingleSubject", null, null },
                    { 87, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 7, 1615m, "SingleSubject", null, null },
                    { 88, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 7, 90m, "SingleSubject", null, null },
                    { 89, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 8, 549m, "AllSubjects", null, null },
                    { 90, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 8, 2965m, "AllSubjects", null, null },
                    { 91, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 8, 4941m, "AllSubjects", null, null },
                    { 92, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 8, 275m, "AllSubjects", null, null },
                    { 93, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 8, 329m, "SingleSubject", null, null },
                    { 94, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 8, 1779m, "SingleSubject", null, null },
                    { 95, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 8, 2965m, "SingleSubject", null, null },
                    { 96, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 8, 165m, "SingleSubject", null, null },
                    { 97, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Monthly", null, false, 9, 899m, "AllSubjects", null, null },
                    { 98, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "SixMonths", null, false, 9, 4855m, "AllSubjects", null, null },
                    { 99, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "TwelveMonths", null, false, 9, 8091m, "AllSubjects", null, null },
                    { 100, "Yks", new DateTime(2026, 11, 8, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "Summer", null, false, 9, 450m, "AllSubjects", null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanPrices_PlanId_Band_GradeLevel_Scope_Duration",
                table: "SubscriptionPlanPrices",
                columns: new[] { "PlanId", "Band", "GradeLevel", "Scope", "Duration" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionPlanPrices");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "UserSubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "Duration",
                table: "UserSubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "PricePaid",
                table: "UserSubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "ScopeSubjectId",
                table: "UserSubscriptionPlans");
        }
    }
}

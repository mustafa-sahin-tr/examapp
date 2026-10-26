using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInteractionGradingAndActivityCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Worksheets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CheckCount",
                table: "TestInstances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstCheckedAt",
                table: "TestInstances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SolutionsRevealedAt",
                table: "TestInstances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectCount",
                table: "TestInstanceQuestions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Feedback",
                table: "TestInstanceQuestions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GradedBy",
                table: "TestInstanceQuestions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalCount",
                table: "TestInstanceQuestions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "CheckCount",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "FirstCheckedAt",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "SolutionsRevealedAt",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "CorrectCount",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "Feedback",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "GradedBy",
                table: "TestInstanceQuestions");

            migrationBuilder.DropColumn(
                name: "TotalCount",
                table: "TestInstanceQuestions");
        }
    }
}

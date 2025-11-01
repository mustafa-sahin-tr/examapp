using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class aggraget_v2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BestCorrectStreak",
                table: "StudentQuestionAggregates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CurrentCorrectStreak",
                table: "StudentQuestionAggregates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAnsweredAtUtc",
                table: "StudentQuestionAggregates",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BestCorrectStreak",
                table: "StudentQuestionAggregates");

            migrationBuilder.DropColumn(
                name: "CurrentCorrectStreak",
                table: "StudentQuestionAggregates");

            migrationBuilder.DropColumn(
                name: "LastAnsweredAtUtc",
                table: "StudentQuestionAggregates");
        }
    }
}

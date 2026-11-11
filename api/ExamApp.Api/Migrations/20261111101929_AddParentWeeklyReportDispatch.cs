using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddParentWeeklyReportDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "WeeklyReportSentFor",
                table: "ParentStudentLinks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WeeklyReportEmailOptOut",
                table: "Parents",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeeklyReportSentFor",
                table: "ParentStudentLinks");

            migrationBuilder.DropColumn(
                name: "WeeklyReportEmailOptOut",
                table: "Parents");
        }
    }
}

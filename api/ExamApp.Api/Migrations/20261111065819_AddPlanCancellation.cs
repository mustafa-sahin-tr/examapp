using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "UserSubscriptionPlans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledByUserId",
                table: "UserSubscriptionPlans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundAmount",
                table: "UserSubscriptionPlans",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "UserSubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "UserSubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "RefundAmount",
                table: "UserSubscriptionPlans");
        }
    }
}

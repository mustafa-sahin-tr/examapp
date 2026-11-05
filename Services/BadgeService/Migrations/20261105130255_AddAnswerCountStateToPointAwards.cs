using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddAnswerCountStateToPointAwards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CountedActivityDate",
                table: "AnswerPointAwards",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CountedCorrect",
                table: "AnswerPointAwards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CountedTimeSeconds",
                table: "AnswerPointAwards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasBeenAnswered",
                table: "AnswerPointAwards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // issue #785: existing rows were already added to the counters. Marking them counted stops a later change from
            // counting the question again. The time they added is unknown, so it stays 0. Inflated totals are left as they are.
            migrationBuilder.Sql("""
                UPDATE "AnswerPointAwards"
                SET "CountedActivityDate" = ("LastAppliedRevisionUtc" AT TIME ZONE 'UTC')::date,
                    "CountedCorrect" = "PointsAwarded" > 0,
                    "HasBeenAnswered" = TRUE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountedActivityDate",
                table: "AnswerPointAwards");

            migrationBuilder.DropColumn(
                name: "CountedCorrect",
                table: "AnswerPointAwards");

            migrationBuilder.DropColumn(
                name: "CountedTimeSeconds",
                table: "AnswerPointAwards");

            migrationBuilder.DropColumn(
                name: "HasBeenAnswered",
                table: "AnswerPointAwards");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnswerKeySoftDeleteAndSolutionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabelingActivitySolutions_CropSha256_InteractionType_PlanHa~",
                table: "LabelingActivitySolutions");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "LabelingAnswerKeyEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByKeycloakId",
                table: "LabelingAnswerKeyEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByName",
                table: "LabelingAnswerKeyEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabelingAnswerKeyEntries_FilePage",
                table: "LabelingAnswerKeyEntries",
                column: "FilePage");

            migrationBuilder.CreateIndex(
                name: "IX_LabelingActivitySolutions_CropSha256_InteractionType_PlanHa~",
                table: "LabelingActivitySolutions",
                columns: new[] { "CropSha256", "InteractionType", "PlanHash", "Mode", "KeyInputHash", "ModelName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabelingAnswerKeyEntries_FilePage",
                table: "LabelingAnswerKeyEntries");

            migrationBuilder.DropIndex(
                name: "IX_LabelingActivitySolutions_CropSha256_InteractionType_PlanHa~",
                table: "LabelingActivitySolutions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "LabelingAnswerKeyEntries");

            migrationBuilder.DropColumn(
                name: "DeletedByKeycloakId",
                table: "LabelingAnswerKeyEntries");

            migrationBuilder.DropColumn(
                name: "DeletedByName",
                table: "LabelingAnswerKeyEntries");

            migrationBuilder.CreateIndex(
                name: "IX_LabelingActivitySolutions_CropSha256_InteractionType_PlanHa~",
                table: "LabelingActivitySolutions",
                columns: new[] { "CropSha256", "InteractionType", "PlanHash", "Mode", "KeyInputHash", "ModelName" },
                unique: true);
        }
    }
}

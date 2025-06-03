using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class TestTopicsNonMandatory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubTopicId",
                table: "Worksheets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TopicId",
                table: "Worksheets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Worksheets_SubTopicId",
                table: "Worksheets",
                column: "SubTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_Worksheets_TopicId",
                table: "Worksheets",
                column: "TopicId");

            migrationBuilder.AddForeignKey(
                name: "FK_Worksheets_SubTopics_SubTopicId",
                table: "Worksheets",
                column: "SubTopicId",
                principalTable: "SubTopics",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Worksheets_Topics_TopicId",
                table: "Worksheets",
                column: "TopicId",
                principalTable: "Topics",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Worksheets_SubTopics_SubTopicId",
                table: "Worksheets");

            migrationBuilder.DropForeignKey(
                name: "FK_Worksheets_Topics_TopicId",
                table: "Worksheets");

            migrationBuilder.DropIndex(
                name: "IX_Worksheets_SubTopicId",
                table: "Worksheets");

            migrationBuilder.DropIndex(
                name: "IX_Worksheets_TopicId",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "SubTopicId",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "TopicId",
                table: "Worksheets");
        }
    }
}

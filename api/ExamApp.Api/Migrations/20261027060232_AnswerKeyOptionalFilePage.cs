using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AnswerKeyOptionalFilePage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabelingAnswerKeyPages_BookId_FilePage",
                table: "LabelingAnswerKeyPages");

            migrationBuilder.AlterColumn<int>(
                name: "FilePage",
                table: "LabelingAnswerKeyPages",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "FilePage",
                table: "LabelingAnswerKeyEntries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateIndex(
                name: "IX_LabelingAnswerKeyPages_BookId_FilePage",
                table: "LabelingAnswerKeyPages",
                columns: new[] { "BookId", "FilePage" },
                unique: true,
                filter: "\"FilePage\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LabelingAnswerKeyPages_BookId_ImageSha256_NoFilePage",
                table: "LabelingAnswerKeyPages",
                columns: new[] { "BookId", "ImageSha256" },
                unique: true,
                filter: "\"FilePage\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabelingAnswerKeyPages_BookId_FilePage",
                table: "LabelingAnswerKeyPages");

            migrationBuilder.DropIndex(
                name: "IX_LabelingAnswerKeyPages_BookId_ImageSha256_NoFilePage",
                table: "LabelingAnswerKeyPages");

            migrationBuilder.AlterColumn<int>(
                name: "FilePage",
                table: "LabelingAnswerKeyPages",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "FilePage",
                table: "LabelingAnswerKeyEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabelingAnswerKeyPages_BookId_FilePage",
                table: "LabelingAnswerKeyPages",
                columns: new[] { "BookId", "FilePage" },
                unique: true);
        }
    }
}

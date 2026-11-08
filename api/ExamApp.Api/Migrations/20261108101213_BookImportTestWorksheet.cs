using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class BookImportTestWorksheet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorksheetId",
                table: "BookImportJobTests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookImportJobTests_WorksheetId",
                table: "BookImportJobTests",
                column: "WorksheetId");

            migrationBuilder.AddForeignKey(
                name: "FK_BookImportJobTests_Worksheets_WorksheetId",
                table: "BookImportJobTests",
                column: "WorksheetId",
                principalTable: "Worksheets",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookImportJobTests_Worksheets_WorksheetId",
                table: "BookImportJobTests");

            migrationBuilder.DropIndex(
                name: "IX_BookImportJobTests_WorksheetId",
                table: "BookImportJobTests");

            migrationBuilder.DropColumn(
                name: "WorksheetId",
                table: "BookImportJobTests");
        }
    }
}

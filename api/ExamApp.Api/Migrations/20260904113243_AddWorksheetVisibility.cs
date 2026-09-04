using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorksheetVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StudentVisibility",
                table: "Worksheets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeacherSharing",
                table: "Worksheets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Worksheets_TeacherSharing_StudentVisibility_GradeId_CreateU~",
                table: "Worksheets",
                columns: new[] { "TeacherSharing", "StudentVisibility", "GradeId", "CreateUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Worksheets_TeacherSharing_StudentVisibility_GradeId_CreateU~",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "StudentVisibility",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "TeacherSharing",
                table: "Worksheets");
        }
    }
}

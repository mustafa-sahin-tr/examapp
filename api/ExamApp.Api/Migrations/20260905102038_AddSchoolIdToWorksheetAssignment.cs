using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolIdToWorksheetAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "WorksheetAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetAssignments_SchoolId",
                table: "WorksheetAssignments",
                column: "SchoolId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorksheetAssignments_Schools_SchoolId",
                table: "WorksheetAssignments",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorksheetAssignments_Schools_SchoolId",
                table: "WorksheetAssignments");

            migrationBuilder.DropIndex(
                name: "IX_WorksheetAssignments_SchoolId",
                table: "WorksheetAssignments");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "WorksheetAssignments");
        }
    }
}

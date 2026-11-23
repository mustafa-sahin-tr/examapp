using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorksheetAssignmentSection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SectionId",
                table: "WorksheetAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetAssignments_SectionId",
                table: "WorksheetAssignments",
                column: "SectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorksheetAssignments_SchoolSections_SectionId",
                table: "WorksheetAssignments",
                column: "SectionId",
                principalTable: "SchoolSections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorksheetAssignments_SchoolSections_SectionId",
                table: "WorksheetAssignments");

            migrationBuilder.DropIndex(
                name: "IX_WorksheetAssignments_SectionId",
                table: "WorksheetAssignments");

            migrationBuilder.DropColumn(
                name: "SectionId",
                table: "WorksheetAssignments");
        }
    }
}

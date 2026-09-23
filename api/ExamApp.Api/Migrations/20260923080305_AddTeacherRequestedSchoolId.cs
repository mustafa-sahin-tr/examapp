using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherRequestedSchoolId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequestedSchoolId",
                table: "Teachers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teachers_RequestedSchoolId",
                table: "Teachers",
                column: "RequestedSchoolId");

            migrationBuilder.AddForeignKey(
                name: "FK_Teachers_Schools_RequestedSchoolId",
                table: "Teachers",
                column: "RequestedSchoolId",
                principalTable: "Schools",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Teachers_Schools_RequestedSchoolId",
                table: "Teachers");

            migrationBuilder.DropIndex(
                name: "IX_Teachers_RequestedSchoolId",
                table: "Teachers");

            migrationBuilder.DropColumn(
                name: "RequestedSchoolId",
                table: "Teachers");
        }
    }
}

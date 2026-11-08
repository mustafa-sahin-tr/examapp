using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMockExamFreeTrial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFreeTrial",
                table: "MockExams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_MockExams_GradeId_FreeTrial",
                table: "MockExams",
                column: "GradeId",
                unique: true,
                filter: "\"IsFreeTrial\" AND NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MockExams_GradeId_FreeTrial",
                table: "MockExams");

            migrationBuilder.DropColumn(
                name: "IsFreeTrial",
                table: "MockExams");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminUserActionLogSchoolIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FromSchoolId",
                table: "AdminUserActionLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToSchoolId",
                table: "AdminUserActionLogs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromSchoolId",
                table: "AdminUserActionLogs");

            migrationBuilder.DropColumn(
                name: "ToSchoolId",
                table: "AdminUserActionLogs");
        }
    }
}

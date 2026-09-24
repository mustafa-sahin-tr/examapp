using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminDataAccessLogOutcomeAndTargetId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "AdminDataAccessLogs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Served");

            migrationBuilder.AddColumn<int>(
                name: "TargetId",
                table: "AdminDataAccessLogs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "AdminDataAccessLogs");

            migrationBuilder.DropColumn(
                name: "TargetId",
                table: "AdminDataAccessLogs");
        }
    }
}

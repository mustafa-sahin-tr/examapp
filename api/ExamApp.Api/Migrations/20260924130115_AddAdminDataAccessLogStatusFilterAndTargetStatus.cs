using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminDataAccessLogStatusFilterAndTargetStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StatusFilter",
                table: "AdminDataAccessLogs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetStatus",
                table: "AdminDataAccessLogs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusFilter",
                table: "AdminDataAccessLogs");

            migrationBuilder.DropColumn(
                name: "TargetStatus",
                table: "AdminDataAccessLogs");
        }
    }
}

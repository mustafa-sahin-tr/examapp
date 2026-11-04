using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeSessionSubTopicsAndTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubTopicIdsJson",
                table: "PracticeSessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetCount",
                table: "PracticeSessions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubTopicIdsJson",
                table: "PracticeSessions");

            migrationBuilder.DropColumn(
                name: "TargetCount",
                table: "PracticeSessions");
        }
    }
}

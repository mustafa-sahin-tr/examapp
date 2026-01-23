using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShowPassageFirstToQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowPassageFirst",
                table: "Questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowPassageFirst",
                table: "Questions");
        }
    }
}

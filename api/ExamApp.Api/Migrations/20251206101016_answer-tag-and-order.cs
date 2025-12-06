using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class answertagandorder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Answers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tag",
                table: "Answers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Order",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "Tag",
                table: "Answers");
        }
    }
}

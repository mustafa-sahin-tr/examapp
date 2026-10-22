using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelingResultPromptHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BasePromptVersion",
                table: "LabelingResults",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptHash",
                table: "LabelingResults",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BasePromptVersion",
                table: "LabelingResults");

            migrationBuilder.DropColumn(
                name: "PromptHash",
                table: "LabelingResults");
        }
    }
}

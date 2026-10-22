using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelingPromptVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PromptVersion",
                table: "LabelingResults",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LabelingPromptVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    QuestionInstruction = table.Column<string>(type: "text", nullable: false),
                    OptionInstruction = table.Column<string>(type: "text", nullable: false),
                    QuestionPadding = table.Column<int>(type: "integer", nullable: false),
                    OptionPadding = table.Column<int>(type: "integer", nullable: false),
                    ExpandQuestionToOptions = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelingPromptVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabelingPromptVersions_Version",
                table: "LabelingPromptVersions",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LabelingPromptVersions");

            migrationBuilder.DropColumn(
                name: "PromptVersion",
                table: "LabelingResults");
        }
    }
}

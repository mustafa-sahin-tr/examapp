using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelingActivityStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabelingActivityPromptVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InteractionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Instruction = table.Column<string>(type: "text", nullable: false),
                    CreatedByKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelingActivityPromptVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LabelingActivityResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CropSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InteractionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptVersion = table.Column<int>(type: "integer", nullable: false),
                    PlanJson = table.Column<string>(type: "jsonb", nullable: false),
                    Instruction = table.Column<string>(type: "text", nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    CreatedByKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelingActivityResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabelingActivityPromptVersions_InteractionType_Version",
                table: "LabelingActivityPromptVersions",
                columns: new[] { "InteractionType", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabelingActivityResults_CropSha256_InteractionType_ModelName",
                table: "LabelingActivityResults",
                columns: new[] { "CropSha256", "InteractionType", "ModelName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LabelingActivityPromptVersions");

            migrationBuilder.DropTable(
                name: "LabelingActivityResults");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelingPagesAndResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabelingPages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelingPages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LabelingResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PageId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BoxesJson = table.Column<string>(type: "jsonb", nullable: false),
                    BaseSource = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    BaseModelName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    CreatedByKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelingResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabelingResults_LabelingPages_PageId",
                        column: x => x.PageId,
                        principalTable: "LabelingPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabelingPages_ImageSha256",
                table: "LabelingPages",
                column: "ImageSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabelingResults_PageId_Source_ModelName",
                table: "LabelingResults",
                columns: new[] { "PageId", "Source", "ModelName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LabelingResults");

            migrationBuilder.DropTable(
                name: "LabelingPages");
        }
    }
}

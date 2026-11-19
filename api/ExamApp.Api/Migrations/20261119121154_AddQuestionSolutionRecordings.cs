using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionSolutionRecordings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestionSolutionRecordings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "integer", nullable: false),
                    StrokesJson = table.Column<string>(type: "text", nullable: false),
                    AudioObjectPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AudioContentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AudioSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    SchemaRevision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionSolutionRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionSolutionRecordings_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSolutionRecordings_QuestionId",
                table: "QuestionSolutionRecordings",
                column: "QuestionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionSolutionRecordings");
        }
    }
}

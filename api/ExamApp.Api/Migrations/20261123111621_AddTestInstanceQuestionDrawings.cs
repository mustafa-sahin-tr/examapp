using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTestInstanceQuestionDrawings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestInstanceQuestionDrawings",
                columns: table => new
                {
                    WorksheetInstanceQuestionId = table.Column<int>(type: "integer", nullable: false),
                    Ink = table.Column<string>(type: "jsonb", nullable: false),
                    ScratchStrokes = table.Column<string>(type: "jsonb", nullable: false),
                    ScratchText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestInstanceQuestionDrawings", x => x.WorksheetInstanceQuestionId);
                    table.ForeignKey(
                        name: "FK_TestInstanceQuestionDrawings_TestInstanceQuestions_Workshee~",
                        column: x => x.WorksheetInstanceQuestionId,
                        principalTable: "TestInstanceQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestInstanceQuestionDrawings");
        }
    }
}

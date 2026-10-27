using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenTextReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpenTextReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorksheetInstanceQuestionId = table.Column<int>(type: "integer", nullable: false),
                    Evaluation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Feedback = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnswerSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Stale = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenTextReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenTextReviews_TestInstanceQuestions_WorksheetInstanceQues~",
                        column: x => x.WorksheetInstanceQuestionId,
                        principalTable: "TestInstanceQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetAssignments_CreateUserId_WorksheetId",
                table: "WorksheetAssignments",
                columns: new[] { "CreateUserId", "WorksheetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_InteractionType_OpenText",
                table: "Questions",
                column: "InteractionType",
                filter: "\"InteractionType\" = 'openText'");

            migrationBuilder.CreateIndex(
                name: "IX_OpenTextReviews_WorksheetInstanceQuestionId",
                table: "OpenTextReviews",
                column: "WorksheetInstanceQuestionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenTextReviews");

            migrationBuilder.DropIndex(
                name: "IX_WorksheetAssignments_CreateUserId_WorksheetId",
                table: "WorksheetAssignments");

            migrationBuilder.DropIndex(
                name: "IX_Questions_InteractionType_OpenText",
                table: "Questions");
        }
    }
}

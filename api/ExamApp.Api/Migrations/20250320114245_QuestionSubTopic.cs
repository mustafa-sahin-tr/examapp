using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class QuestionSubTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Questions_SubTopics_SubTopicId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_SubTopicId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SubTopicId",
                table: "Questions");

            migrationBuilder.CreateTable(
                name: "QuestionSubTopics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    SubTopicId = table.Column<int>(type: "integer", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdateUserId = table.Column<int>(type: "integer", nullable: true),
                    DeleteTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeleteUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionSubTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionSubTopics_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionSubTopics_SubTopics_SubTopicId",
                        column: x => x.SubTopicId,
                        principalTable: "SubTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSubTopics_QuestionId",
                table: "QuestionSubTopics",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSubTopics_SubTopicId",
                table: "QuestionSubTopics",
                column: "SubTopicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionSubTopics");

            migrationBuilder.AddColumn<int>(
                name: "SubTopicId",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_SubTopicId",
                table: "Questions",
                column: "SubTopicId");

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_SubTopics_SubTopicId",
                table: "Questions",
                column: "SubTopicId",
                principalTable: "SubTopics",
                principalColumn: "Id");
        }
    }
}

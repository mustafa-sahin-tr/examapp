using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorksheetCommentsAndCommentsToggle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CommentsEnabled",
                table: "Worksheets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CommentsEnabledOverride",
                table: "WorksheetAssignments",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorksheetComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorksheetId = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: true),
                    AuthorUserId = table.Column<int>(type: "integer", nullable: false),
                    AuthorKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AuthorRole = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResponsibleTeacherUserId = table.Column<int>(type: "integer", nullable: true),
                    ParentCommentId = table.Column<int>(type: "integer", nullable: true),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
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
                    table.PrimaryKey("PK_WorksheetComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorksheetComments_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorksheetComments_WorksheetComments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "WorksheetComments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorksheetComments_Worksheets_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "Worksheets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetComments_ParentCommentId_CreateTime_Id",
                table: "WorksheetComments",
                columns: new[] { "ParentCommentId", "CreateTime", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetComments_QuestionId",
                table: "WorksheetComments",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetComments_Roots",
                table: "WorksheetComments",
                columns: new[] { "WorksheetId", "QuestionId", "CreateTime", "Id" },
                filter: "\"ParentCommentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetComments_WorksheetId_QuestionId_CreateTime_Id",
                table: "WorksheetComments",
                columns: new[] { "WorksheetId", "QuestionId", "CreateTime", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorksheetComments");

            migrationBuilder.DropColumn(
                name: "CommentsEnabled",
                table: "Worksheets");

            migrationBuilder.DropColumn(
                name: "CommentsEnabledOverride",
                table: "WorksheetAssignments");
        }
    }
}

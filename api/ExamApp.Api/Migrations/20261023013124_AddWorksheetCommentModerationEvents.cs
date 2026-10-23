using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorksheetCommentModerationEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorksheetCommentModerationEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorksheetCommentId = table.Column<int>(type: "integer", nullable: true),
                    WorksheetId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActorSchoolId = table.Column<int>(type: "integer", nullable: true),
                    CommentAuthorSchoolId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorksheetCommentModerationEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetCommentModerationEvents_Action_CreatedAt",
                table: "WorksheetCommentModerationEvents",
                columns: new[] { "Action", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetCommentModerationEvents_ActorUserId_CreatedAt",
                table: "WorksheetCommentModerationEvents",
                columns: new[] { "ActorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetCommentModerationEvents_WorksheetCommentId_Created~",
                table: "WorksheetCommentModerationEvents",
                columns: new[] { "WorksheetCommentId", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorksheetCommentModerationEvents");
        }
    }
}

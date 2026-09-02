using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorksheetReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorksheetReminders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorksheetId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemindBeforeMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    HangfireJobId = table.Column<string>(type: "text", nullable: true),
                    StudentKeycloakId = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_WorksheetReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorksheetReminders_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorksheetReminders_Worksheets_WorksheetId",
                        column: x => x.WorksheetId,
                        principalTable: "Worksheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetReminders_StudentId",
                table: "WorksheetReminders",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetReminders_WorksheetId_StudentId",
                table: "WorksheetReminders",
                columns: new[] { "WorksheetId", "StudentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorksheetReminders");
        }
    }
}

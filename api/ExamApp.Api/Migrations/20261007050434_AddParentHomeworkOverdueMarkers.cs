using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddParentHomeworkOverdueMarkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParentHomeworkOverdueMarkers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WorksheetId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NotifiedParentCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentHomeworkOverdueMarkers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorksheetAssignments_EndAt_NotNull",
                table: "WorksheetAssignments",
                column: "EndAt",
                filter: "\"EndAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParentHomeworkOverdueMarkers_ProcessedAt",
                table: "ParentHomeworkOverdueMarkers",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ParentHomeworkOverdueMarkers_WorksheetId_StudentId",
                table: "ParentHomeworkOverdueMarkers",
                columns: new[] { "WorksheetId", "StudentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParentHomeworkOverdueMarkers");

            migrationBuilder.DropIndex(
                name: "IX_WorksheetAssignments_EndAt_NotNull",
                table: "WorksheetAssignments");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchoolAnnouncements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    SenderUserId = table.Column<int>(type: "integer", nullable: false),
                    Target = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetGradeId = table.Column<int>(type: "integer", nullable: true),
                    TargetSectionId = table.Column<int>(type: "integer", nullable: true),
                    TargetLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    StudentCount = table.Column<int>(type: "integer", nullable: false),
                    TeacherCount = table.Column<int>(type: "integer", nullable: false),
                    ParentCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolAnnouncements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchoolAnnouncements_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchoolAnnouncements_SchoolId",
                table: "SchoolAnnouncements",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_SchoolAnnouncements_SenderUserId_CreatedAt",
                table: "SchoolAnnouncements",
                columns: new[] { "SenderUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchoolAnnouncements");
        }
    }
}

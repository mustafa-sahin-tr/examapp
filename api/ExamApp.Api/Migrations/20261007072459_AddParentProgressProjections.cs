using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddParentProgressProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProgressResetAtUtc",
                table: "Students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StudentBadgeProjections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    BadgeDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EarnedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentBadgeProjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentBadgeProjections_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentDailyXps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentDailyXps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentDailyXps_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentBadgeProjections_StudentId_BadgeDefinitionId",
                table: "StudentBadgeProjections",
                columns: new[] { "StudentId", "BadgeDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentDailyXps_StudentId_Day",
                table: "StudentDailyXps",
                columns: new[] { "StudentId", "Day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentBadgeProjections");

            migrationBuilder.DropTable(
                name: "StudentDailyXps");

            migrationBuilder.DropColumn(
                name: "ProgressResetAtUtc",
                table: "Students");
        }
    }
}

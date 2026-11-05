using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetPrograms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TargetPrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScoreType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Institution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    MinimumScore = table.Column<decimal>(type: "numeric(8,5)", precision: 8, scale: 5, nullable: false),
                    Percentile = table.Column<decimal>(type: "numeric(8,5)", precision: 8, scale: 5, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NormalizedKey = table.Column<string>(type: "character varying(650)", maxLength: 650, nullable: false),
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
                    table.PrimaryKey("PK_TargetPrograms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StudentTargetGoals",
                columns: table => new
                {
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    TargetProgramId = table.Column<int>(type: "integer", nullable: false),
                    SelectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentTargetGoals", x => x.StudentId);
                    table.ForeignKey(
                        name: "FK_StudentTargetGoals_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentTargetGoals_TargetPrograms_TargetProgramId",
                        column: x => x.TargetProgramId,
                        principalTable: "TargetPrograms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentTargetGoals_TargetProgramId",
                table: "StudentTargetGoals",
                column: "TargetProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_TargetPrograms_ScoreType_Year_NormalizedKey",
                table: "TargetPrograms",
                columns: new[] { "ScoreType", "Year", "NormalizedKey" },
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentTargetGoals");

            migrationBuilder.DropTable(
                name: "TargetPrograms");
        }
    }
}

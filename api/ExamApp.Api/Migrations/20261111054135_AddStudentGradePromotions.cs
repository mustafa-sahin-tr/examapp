using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentGradePromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentGradePromotions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    FromGradeId = table.Column<int>(type: "integer", nullable: false),
                    ToGradeId = table.Column<int>(type: "integer", nullable: false),
                    PromotedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevertedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevertedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentGradePromotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentGradePromotions_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentGradePromotions_StudentId_Year",
                table: "StudentGradePromotions",
                columns: new[] { "StudentId", "Year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentGradePromotions");
        }
    }
}

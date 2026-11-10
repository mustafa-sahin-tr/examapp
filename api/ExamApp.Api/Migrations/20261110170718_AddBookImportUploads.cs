using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookImportUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookImportUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BookName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GradeId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PartSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PartCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookImportUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookImportUploads_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookImportUploads_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookImportUploads_CreatedAt",
                table: "BookImportUploads",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BookImportUploads_GradeId",
                table: "BookImportUploads",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_BookImportUploads_SubjectId",
                table: "BookImportUploads",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookImportUploads");
        }
    }
}

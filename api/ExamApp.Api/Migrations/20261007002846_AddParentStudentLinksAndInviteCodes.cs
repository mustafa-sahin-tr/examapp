using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddParentStudentLinksAndInviteCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParentInviteCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsedByParentId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentInviteCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParentInviteCodes_Parents_UsedByParentId",
                        column: x => x.UsedByParentId,
                        principalTable: "Parents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ParentInviteCodes_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ParentStudentLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ParentId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentStudentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParentStudentLinks_Parents_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Parents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ParentStudentLinks_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParentInviteCodes_CodeHash_Unused",
                table: "ParentInviteCodes",
                column: "CodeHash",
                unique: true,
                filter: "\"UsedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParentInviteCodes_StudentId_ExpiresAt",
                table: "ParentInviteCodes",
                columns: new[] { "StudentId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentInviteCodes_UsedByParentId",
                table: "ParentInviteCodes",
                column: "UsedByParentId");

            migrationBuilder.CreateIndex(
                name: "IX_ParentStudentLinks_Open_Pair",
                table: "ParentStudentLinks",
                columns: new[] { "ParentId", "StudentId" },
                unique: true,
                filter: "\"Status\" IN ('Active', 'Pending')");

            migrationBuilder.CreateIndex(
                name: "IX_ParentStudentLinks_ParentId_Status",
                table: "ParentStudentLinks",
                columns: new[] { "ParentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentStudentLinks_StudentId_Status",
                table: "ParentStudentLinks",
                columns: new[] { "StudentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParentInviteCodes");

            migrationBuilder.DropTable(
                name: "ParentStudentLinks");
        }
    }
}

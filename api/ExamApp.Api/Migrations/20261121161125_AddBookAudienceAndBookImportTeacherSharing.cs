using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAudienceAndBookImportTeacherSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeacherSharing",
                table: "BookImportJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BookAudienceMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookId = table.Column<int>(type: "integer", nullable: false),
                    SchoolId = table.Column<int>(type: "integer", nullable: true),
                    TeacherUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookAudienceMembers", x => x.Id);
                    table.CheckConstraint("CK_BookAudienceMembers_OneTarget", "(\"SchoolId\" IS NULL) <> (\"TeacherUserId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_BookAudienceMembers_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookAudienceMembers_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookAudienceMembers_BookId_SchoolId",
                table: "BookAudienceMembers",
                columns: new[] { "BookId", "SchoolId" },
                unique: true,
                filter: "\"SchoolId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookAudienceMembers_BookId_TeacherUserId",
                table: "BookAudienceMembers",
                columns: new[] { "BookId", "TeacherUserId" },
                unique: true,
                filter: "\"TeacherUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookAudienceMembers_SchoolId",
                table: "BookAudienceMembers",
                column: "SchoolId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookAudienceMembers");

            migrationBuilder.DropColumn(
                name: "TeacherSharing",
                table: "BookImportJobs");
        }
    }
}

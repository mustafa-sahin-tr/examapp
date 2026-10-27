using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CanonicalBookId",
                table: "Books",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Books",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookAnswerKeyRanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookId = table.Column<int>(type: "integer", nullable: false),
                    FileFrom = table.Column<int>(type: "integer", nullable: false),
                    FileTo = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookAnswerKeyRanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookAnswerKeyRanges_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookPageOffsets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookId = table.Column<int>(type: "integer", nullable: false),
                    FileFrom = table.Column<int>(type: "integer", nullable: false),
                    FileTo = table.Column<int>(type: "integer", nullable: false),
                    Offset = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookPageOffsets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookPageOffsets_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookTocEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookId = table.Column<int>(type: "integer", nullable: false),
                    FileFrom = table.Column<int>(type: "integer", nullable: false),
                    FileTo = table.Column<int>(type: "integer", nullable: false),
                    GradeId = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<int>(type: "integer", nullable: false),
                    TopicId = table.Column<int>(type: "integer", nullable: true),
                    SubTopicId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookTocEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookTocEntries_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookTocEntries_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookTocEntries_SubTopics_SubTopicId",
                        column: x => x.SubTopicId,
                        principalTable: "SubTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookTocEntries_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookTocEntries_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuestionSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    BookId = table.Column<int>(type: "integer", nullable: false),
                    FilePage = table.Column<int>(type: "integer", nullable: false),
                    PrintedPage = table.Column<int>(type: "integer", nullable: true),
                    ActivityLabel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ItemLabel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CropSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionSources_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuestionSources_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Books_CanonicalBookId",
                table: "Books",
                column: "CanonicalBookId");

            migrationBuilder.CreateIndex(
                name: "IX_BookAnswerKeyRanges_BookId_FileFrom",
                table: "BookAnswerKeyRanges",
                columns: new[] { "BookId", "FileFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_BookPageOffsets_BookId_FileFrom",
                table: "BookPageOffsets",
                columns: new[] { "BookId", "FileFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_BookTocEntries_BookId_FileFrom",
                table: "BookTocEntries",
                columns: new[] { "BookId", "FileFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_BookTocEntries_GradeId",
                table: "BookTocEntries",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_BookTocEntries_SubjectId",
                table: "BookTocEntries",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BookTocEntries_SubTopicId",
                table: "BookTocEntries",
                column: "SubTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_BookTocEntries_TopicId",
                table: "BookTocEntries",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSources_BookId",
                table: "QuestionSources",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSources_CropSha256",
                table: "QuestionSources",
                column: "CropSha256");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSources_QuestionId",
                table: "QuestionSources",
                column: "QuestionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Books_Books_CanonicalBookId",
                table: "Books",
                column: "CanonicalBookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_Books_CanonicalBookId",
                table: "Books");

            migrationBuilder.DropTable(
                name: "BookAnswerKeyRanges");

            migrationBuilder.DropTable(
                name: "BookPageOffsets");

            migrationBuilder.DropTable(
                name: "BookTocEntries");

            migrationBuilder.DropTable(
                name: "QuestionSources");

            migrationBuilder.DropIndex(
                name: "IX_Books_CanonicalBookId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "CanonicalBookId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Books");
        }
    }
}

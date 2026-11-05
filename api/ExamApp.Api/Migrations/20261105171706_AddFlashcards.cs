using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFlashcards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlashcardDecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: false),
                    OwnerKind = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    GradeId = table.Column<int>(type: "integer", nullable: true),
                    SubjectId = table.Column<int>(type: "integer", nullable: true),
                    TopicId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_FlashcardDecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlashcardDecks_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlashcardDecks_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlashcardDecks_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FlashcardDeckAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeckId = table.Column<int>(type: "integer", nullable: false),
                    StudentId = table.Column<int>(type: "integer", nullable: true),
                    GradeId = table.Column<int>(type: "integer", nullable: true),
                    SchoolId = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_FlashcardDeckAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlashcardDeckAssignments_FlashcardDecks_DeckId",
                        column: x => x.DeckId,
                        principalTable: "FlashcardDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FlashcardDeckAssignments_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlashcardDeckAssignments_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlashcardDeckAssignments_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Flashcards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeckId = table.Column<int>(type: "integer", nullable: false),
                    Front = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Back = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_Flashcards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Flashcards_FlashcardDecks_DeckId",
                        column: x => x.DeckId,
                        principalTable: "FlashcardDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlashcardReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentId = table.Column<int>(type: "integer", nullable: false),
                    FlashcardId = table.Column<int>(type: "integer", nullable: false),
                    Box = table.Column<int>(type: "integer", nullable: false),
                    DueDay = table.Column<DateOnly>(type: "date", nullable: false),
                    LastReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashcardReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlashcardReviews_Flashcards_FlashcardId",
                        column: x => x.FlashcardId,
                        principalTable: "Flashcards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FlashcardReviews_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDeckAssignments_DeckId_GradeId_SchoolId",
                table: "FlashcardDeckAssignments",
                columns: new[] { "DeckId", "GradeId", "SchoolId" },
                unique: true,
                filter: "\"StudentId\" IS NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDeckAssignments_DeckId_StudentId",
                table: "FlashcardDeckAssignments",
                columns: new[] { "DeckId", "StudentId" },
                unique: true,
                filter: "\"StudentId\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDeckAssignments_GradeId_SchoolId",
                table: "FlashcardDeckAssignments",
                columns: new[] { "GradeId", "SchoolId" });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDeckAssignments_SchoolId",
                table: "FlashcardDeckAssignments",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDeckAssignments_StudentId",
                table: "FlashcardDeckAssignments",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDecks_GradeId",
                table: "FlashcardDecks",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDecks_OwnerKind_OwnerUserId",
                table: "FlashcardDecks",
                columns: new[] { "OwnerKind", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDecks_SubjectId",
                table: "FlashcardDecks",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardDecks_TopicId",
                table: "FlashcardDecks",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_FlashcardId",
                table: "FlashcardReviews",
                column: "FlashcardId");

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_StudentId_DueDay",
                table: "FlashcardReviews",
                columns: new[] { "StudentId", "DueDay" });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_StudentId_FlashcardId",
                table: "FlashcardReviews",
                columns: new[] { "StudentId", "FlashcardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Flashcards_DeckId_Order",
                table: "Flashcards",
                columns: new[] { "DeckId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlashcardDeckAssignments");

            migrationBuilder.DropTable(
                name: "FlashcardReviews");

            migrationBuilder.DropTable(
                name: "Flashcards");

            migrationBuilder.DropTable(
                name: "FlashcardDecks");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddAnswerPointAwardAndConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // issue #279 (item 1): xmin is Postgres's own hidden system column, already present on every
            // table — mapping it as a shadow concurrency-token property (BadgeDbContext.OnModelCreating)
            // does NOT require creating a column. The scaffolded tool didn't know that (no
            // UseXminAsConcurrencyToken() helper in this Npgsql.EntityFrameworkCore.PostgreSQL version, see
            // BadgeDbContext comment) and emitted AddColumn<uint>("xmin", ...) for the three aggregate
            // tables — running that against real Postgres would fail with
            // 'column "xmin" conflicts with a system column name'. Those three AddColumn/DropColumn calls
            // were hand-removed (ef-migration skill exception: generated-file edit is required here, not a
            // logic change — see PR/report). Everything else below is untouched scaffolder output.

            migrationBuilder.CreateTable(
                name: "AnswerPointAwards",
                columns: table => new
                {
                    TestInstanceId = table.Column<int>(type: "integer", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TestInstanceQuestionId = table.Column<int>(type: "integer", nullable: false),
                    LastAppliedRevision = table.Column<int>(type: "integer", nullable: false),
                    LastAppliedRevisionUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PointsAwarded = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerPointAwards", x => new { x.TestInstanceId, x.QuestionId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedAnswerSubmissions_ProcessedAt",
                table: "ProcessedAnswerSubmissions",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedAnswerSubmissions_UserId",
                table: "ProcessedAnswerSubmissions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerPointAwards_UserId",
                table: "AnswerPointAwards",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnswerPointAwards");

            migrationBuilder.DropIndex(
                name: "IX_ProcessedAnswerSubmissions_ProcessedAt",
                table: "ProcessedAnswerSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_ProcessedAnswerSubmissions_UserId",
                table: "ProcessedAnswerSubmissions");

            // No DropColumn for "xmin" — see the note in Up(); nothing was added, nothing to drop.
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAnswerKeyAndSolutionApproval : Migration
    {
        /// <summary>Issue #580: veri adımı (elle eklendi; uygulama koduna bağlı değil).</summary>
        internal const string BackfillSql =
            "UPDATE \"Questions\" SET \"SolutionSource\" = 'Human', \"SolutionStatus\" = 'Approved' " +
            "WHERE \"InteractionSolution\" IS NOT NULL AND trim(\"InteractionSolution\") <> '' " +
            "AND lower(COALESCE(\"InteractionType\", 'mcq')) <> 'mcq' AND trim(COALESCE(\"InteractionType\", '')) <> '';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SolutionApprovedAt",
                table: "Questions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SolutionApprovedByKeycloakId",
                table: "Questions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SolutionApprovedByName",
                table: "Questions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SolutionPromptVersion",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SolutionSource",
                table: "Questions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "SolutionStatus",
                table: "Questions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.CreateTable(
                name: "LabelingActivitySolutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CropSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InteractionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlanHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanJson = table.Column<string>(type: "jsonb", nullable: false),
                    Mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    KeyInputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyEntryIdsJson = table.Column<string>(type: "text", nullable: true),
                    ModelName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptVersion = table.Column<int>(type: "integer", nullable: true),
                    SolutionJson = table.Column<string>(type: "jsonb", nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    CreatedByKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EditedByKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EditedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelingActivitySolutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LabelingAnswerKeyPages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BookId = table.Column<int>(type: "integer", nullable: false),
                    FilePage = table.Column<int>(type: "integer", nullable: false),
                    ImageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptVersion = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    CreatedByKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelingAnswerKeyPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabelingAnswerKeyPages_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LabelingAnswerKeyEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PageId = table.Column<int>(type: "integer", nullable: false),
                    BookId = table.Column<int>(type: "integer", nullable: false),
                    FilePage = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    PrintedPage = table.Column<int>(type: "integer", nullable: true),
                    ActivityLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ItemLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AnswerText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Visual = table.Column<bool>(type: "boolean", nullable: false),
                    BoxX = table.Column<int>(type: "integer", nullable: true),
                    BoxY = table.Column<int>(type: "integer", nullable: true),
                    BoxWidth = table.Column<int>(type: "integer", nullable: true),
                    BoxHeight = table.Column<int>(type: "integer", nullable: true),
                    CropJpeg = table.Column<byte[]>(type: "bytea", nullable: true),
                    CropSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EditedByKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EditedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabelingAnswerKeyEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabelingAnswerKeyEntries_LabelingAnswerKeyPages_PageId",
                        column: x => x.PageId,
                        principalTable: "LabelingAnswerKeyPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_SolutionStatus_Pending",
                table: "Questions",
                column: "SolutionStatus",
                filter: "\"SolutionStatus\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_LabelingActivitySolutions_CropSha256_InteractionType_PlanHa~",
                table: "LabelingActivitySolutions",
                columns: new[] { "CropSha256", "InteractionType", "PlanHash", "Mode", "KeyInputHash", "ModelName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabelingAnswerKeyEntries_BookId_PrintedPage",
                table: "LabelingAnswerKeyEntries",
                columns: new[] { "BookId", "PrintedPage" });

            migrationBuilder.CreateIndex(
                name: "IX_LabelingAnswerKeyEntries_PageId_Order",
                table: "LabelingAnswerKeyEntries",
                columns: new[] { "PageId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_LabelingAnswerKeyPages_BookId_FilePage",
                table: "LabelingAnswerKeyPages",
                columns: new[] { "BookId", "FilePage" },
                unique: true);

            // Issue #580 (veri adımı, elle eklendi): çözümü olan etkileşimli mevcut sorular öğretmen yazdığı için Human / Approved
            // (yeni kolonların varsayılanı None / Approved). Metin migration'da sabit; testler Data/QuestionSolutionBackfill.Sql ile
            // eşitliğini denetler ve SQLite'ta koşturur.
            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LabelingActivitySolutions");

            migrationBuilder.DropTable(
                name: "LabelingAnswerKeyEntries");

            migrationBuilder.DropTable(
                name: "LabelingAnswerKeyPages");

            migrationBuilder.DropIndex(
                name: "IX_Questions_SolutionStatus_Pending",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SolutionApprovedAt",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SolutionApprovedByKeycloakId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SolutionApprovedByName",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SolutionPromptVersion",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SolutionSource",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SolutionStatus",
                table: "Questions");
        }
    }
}

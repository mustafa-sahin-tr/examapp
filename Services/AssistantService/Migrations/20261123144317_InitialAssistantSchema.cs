using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AssistantService.Migrations
{
    /// <inheritdoc />
    public partial class InitialAssistantSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSub = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserSub = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Messages = table.Column<int>(type: "integer", nullable: false),
                    ImageExplanations = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ContentJson = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CacheReadTokens = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PendingActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserSub = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Tool = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "text", nullable: false),
                    PreviewJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingActions_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Feedback",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    UserSub = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Feedback_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UpdatedAt",
                table: "Conversations",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserSub_UpdatedAt",
                table: "Conversations",
                columns: new[] { "UserSub", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Feedback_MessageId_UserSub",
                table: "Feedback",
                columns: new[] { "MessageId", "UserSub" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_Id",
                table: "Messages",
                columns: new[] { "ConversationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingActions_ConversationId",
                table: "PendingActions",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingActions_UserSub_Status",
                table: "PendingActions",
                columns: new[] { "UserSub", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Usage_Day",
                table: "Usage",
                column: "Day");

            migrationBuilder.CreateIndex(
                name: "IX_Usage_UserSub_Day",
                table: "Usage",
                columns: new[] { "UserSub", "Day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Feedback");

            migrationBuilder.DropTable(
                name: "PendingActions");

            migrationBuilder.DropTable(
                name: "Usage");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Conversations");
        }
    }
}

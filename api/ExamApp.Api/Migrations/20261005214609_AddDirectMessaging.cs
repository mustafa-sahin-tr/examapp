using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentUserId = table.Column<int>(type: "integer", nullable: false),
                    TeacherUserId = table.Column<int>(type: "integer", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DirectMessageBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeacherUserId = table.Column<int>(type: "integer", nullable: false),
                    StudentUserId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_DirectMessageBlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DirectMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationId = table.Column<int>(type: "integer", nullable: false),
                    SenderUserId = table.Column<int>(type: "integer", nullable: false),
                    SenderRole = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_DirectMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DirectMessages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DirectMessageReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationId = table.Column<int>(type: "integer", nullable: false),
                    MessageId = table.Column<int>(type: "integer", nullable: true),
                    ReporterUserId = table.Column<int>(type: "integer", nullable: false),
                    ReporterRole = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
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
                    table.PrimaryKey("PK_DirectMessageReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DirectMessageReports_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DirectMessageReports_DirectMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "DirectMessages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_StudentUserId_TeacherUserId",
                table: "Conversations",
                columns: new[] { "StudentUserId", "TeacherUserId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TeacherUserId_LastMessageAt",
                table: "Conversations",
                columns: new[] { "TeacherUserId", "LastMessageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessageBlocks_TeacherUserId_StudentUserId",
                table: "DirectMessageBlocks",
                columns: new[] { "TeacherUserId", "StudentUserId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessageReports_Conversation_Reporter",
                table: "DirectMessageReports",
                columns: new[] { "ConversationId", "ReporterUserId" },
                unique: true,
                filter: "\"MessageId\" IS NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessageReports_Message_Reporter",
                table: "DirectMessageReports",
                columns: new[] { "MessageId", "ReporterUserId" },
                unique: true,
                filter: "\"MessageId\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessageReports_Status_CreateTime",
                table: "DirectMessageReports",
                columns: new[] { "Status", "CreateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessages_ConversationId_Id",
                table: "DirectMessages",
                columns: new[] { "ConversationId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessages_Unread",
                table: "DirectMessages",
                columns: new[] { "ConversationId", "SenderUserId" },
                filter: "\"ReadAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectMessageBlocks");

            migrationBuilder.DropTable(
                name: "DirectMessageReports");

            migrationBuilder.DropTable(
                name: "DirectMessages");

            migrationBuilder.DropTable(
                name: "Conversations");
        }
    }
}

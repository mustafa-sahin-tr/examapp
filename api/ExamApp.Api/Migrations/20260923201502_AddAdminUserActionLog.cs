using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminUserActionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminUserActionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetId = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUserActionLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminUserActionLogs_ActorKeycloakId_OccurredAtUtc",
                table: "AdminUserActionLogs",
                columns: new[] { "ActorKeycloakId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminUserActionLogs_OccurredAtUtc",
                table: "AdminUserActionLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdminUserActionLogs_TargetType_TargetId_OccurredAtUtc",
                table: "AdminUserActionLogs",
                columns: new[] { "TargetType", "TargetId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminUserActionLogs");
        }
    }
}

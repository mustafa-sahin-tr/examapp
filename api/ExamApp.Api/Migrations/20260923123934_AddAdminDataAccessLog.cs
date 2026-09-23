using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminDataAccessLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminDataAccessLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorKeycloakId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Resource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SchoolIdFilter = table.Column<int>(type: "integer", nullable: true),
                    UnassignedFilter = table.Column<bool>(type: "boolean", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    PageSize = table.Column<int>(type: "integer", nullable: false),
                    ReturnedCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCount = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminDataAccessLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminDataAccessLogs_ActorKeycloakId_OccurredAtUtc",
                table: "AdminDataAccessLogs",
                columns: new[] { "ActorKeycloakId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminDataAccessLogs_OccurredAtUtc",
                table: "AdminDataAccessLogs",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminDataAccessLogs");
        }
    }
}

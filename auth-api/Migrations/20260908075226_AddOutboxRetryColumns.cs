using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRetryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: "OutboxMessages" already exists in the identity DB (created by the
            // 20250501203010_outboxpatternchanges / 20250513093603_outbox_changes migrations),
            // but AppDbContext never had a DbSet<OutboxMessage> until now, so the model
            // snapshot never tracked the table. The scaffolded migration therefore emitted a
            // full CreateTable (which would fail against the real DB where the table already
            // exists) instead of the intended AddColumn diff. Hand-edited to only add the three
            // new columns — everything else (Id/Type/Content/CreatedAt/ProcessedAt) is unchanged.
            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "OutboxMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Error",
                table: "OutboxMessages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "Error",
                table: "OutboxMessages");
        }
    }
}

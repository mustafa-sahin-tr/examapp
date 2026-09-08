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
            // "OutboxMessages" is *supposed* to already exist in the identity DB (created by
            // 20250501203010_outboxpatternchanges / 20250513093603_outbox_changes) — AppDbContext
            // never had a DbSet<OutboxMessage> until now, so nobody ever actually ran those
            // migrations' Up() against a real dev DB, or a dev DB drifted (table manually dropped
            // without reverting migration history). Either way, `Database.Migrate()` on a
            // dev machine where that history entry is present but the table is missing throws
            // "relation OutboxMessages does not exist" here. Written defensively with raw SQL so
            // it succeeds whether the table already exists (adds the 3 columns) or not (creates
            // the full table matching ExamApp.Foundation.Persistence.OutboxMessage first).
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "OutboxMessages" (
                    "Id" uuid NOT NULL,
                    "Type" text NOT NULL,
                    "Content" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "ProcessedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_OutboxMessages" PRIMARY KEY ("Id")
                );

                ALTER TABLE "OutboxMessages" ADD COLUMN IF NOT EXISTS "RetryCount" integer NOT NULL DEFAULT 0;
                ALTER TABLE "OutboxMessages" ADD COLUMN IF NOT EXISTS "NextAttemptAt" timestamp with time zone NULL;
                ALTER TABLE "OutboxMessages" ADD COLUMN IF NOT EXISTS "Error" text NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "OutboxMessages" DROP COLUMN IF EXISTS "RetryCount";
                ALTER TABLE "OutboxMessages" DROP COLUMN IF EXISTS "NextAttemptAt";
                ALTER TABLE "OutboxMessages" DROP COLUMN IF EXISTS "Error";
                """);
        }
    }
}

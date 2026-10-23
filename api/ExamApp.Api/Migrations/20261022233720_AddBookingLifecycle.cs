using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Bookings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByRole",
                table: "Bookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledByUserId",
                table: "Bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledFromStatus",
                table: "Bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoShowParty",
                table: "Bookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NoShowReportedAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoShowReportedByUserId",
                table: "Bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProposedAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedByRole",
                table: "Bookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProposedByUserId",
                table: "Bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProposedSlotId",
                table: "Bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RescheduleCount",
                table: "Bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RescheduleProposalCount",
                table: "Bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ProposedSlotId",
                table: "Bookings",
                column: "ProposedSlotId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_TeacherAvailabilitySlots_ProposedSlotId",
                table: "Bookings",
                column: "ProposedSlotId",
                principalTable: "TeacherAvailabilitySlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_TeacherAvailabilitySlots_ProposedSlotId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_ProposedSlotId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelledByRole",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelledFromStatus",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NoShowParty",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NoShowReportedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NoShowReportedByUserId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ProposedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ProposedByRole",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ProposedByUserId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ProposedSlotId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RescheduleCount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "RescheduleProposalCount",
                table: "Bookings");
        }
    }
}

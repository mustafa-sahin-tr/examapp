using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingParentApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_AvailabilitySlotId",
                table: "Bookings");

            migrationBuilder.AddColumn<DateTime>(
                name: "ParentApprovedAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentApprovedBy",
                table: "Bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentRejectedBy",
                table: "Bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_AvailabilitySlotId",
                table: "Bookings",
                column: "AvailabilitySlotId",
                unique: true,
                filter: "\"Status\" IN (0, 1, 3) AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CreatedAt_AwaitingParent",
                table: "Bookings",
                column: "CreatedAt",
                filter: "\"Status\" = 3 AND NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // issue #439 review: eski kod AwaitingParent(3)/Expired(4)/ParentRejected(5) değerlerini tanımıyor — geri alırken bu
            // satırlar kapalı (Rejected=2) sayılır; böylece eski enum'a eşlenemeyen değer kalmaz ve eski indeks (0,1) bunları
            // aktif saymaz.
            migrationBuilder.Sql("UPDATE \"Bookings\" SET \"Status\" = 2 WHERE \"Status\" IN (3, 4, 5);");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_AvailabilitySlotId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_CreatedAt_AwaitingParent",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ParentApprovedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ParentApprovedBy",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ParentRejectedBy",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_AvailabilitySlotId",
                table: "Bookings",
                column: "AvailabilitySlotId",
                unique: true,
                filter: "\"Status\" IN (0, 1) AND NOT \"IsDeleted\"");
        }
    }
}

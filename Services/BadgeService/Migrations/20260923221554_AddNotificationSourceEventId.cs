using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSourceEventId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_Type_SourceTeacherApplicationId",
                table: "Notifications");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEventId",
                table: "Notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type_SourceEventId",
                table: "Notifications",
                columns: new[] { "Type", "SourceEventId" },
                unique: true,
                filter: "\"SourceEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type_SourceTeacherApplicationId",
                table: "Notifications",
                columns: new[] { "Type", "SourceTeacherApplicationId" },
                unique: true,
                filter: "\"SourceTeacherApplicationId\" IS NOT NULL AND \"Type\" = 'TeacherApplicationSubmitted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_Type_SourceEventId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_Type_SourceTeacherApplicationId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "SourceEventId",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type_SourceTeacherApplicationId",
                table: "Notifications",
                columns: new[] { "Type", "SourceTeacherApplicationId" },
                unique: true,
                filter: "\"SourceTeacherApplicationId\" IS NOT NULL");
        }
    }
}

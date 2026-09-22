using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringAvailabilityRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecurringAvailabilityRuleId",
                table: "TeacherAvailabilitySlots",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringAvailabilityRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeacherId = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_RecurringAvailabilityRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringAvailabilityRules_Teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeacherAvailabilitySlots_RecurringAvailabilityRuleId_Date",
                table: "TeacherAvailabilitySlots",
                columns: new[] { "RecurringAvailabilityRuleId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringAvailabilityRules_TeacherId_DayOfWeek",
                table: "RecurringAvailabilityRules",
                columns: new[] { "TeacherId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringAvailabilityRules_TeacherId_DayOfWeek_StartTime_En~",
                table: "RecurringAvailabilityRules",
                columns: new[] { "TeacherId", "DayOfWeek", "StartTime", "EndTime", "EffectiveFrom" },
                unique: true,
                filter: "\"IsActive\" AND NOT \"IsDeleted\"");

            migrationBuilder.AddForeignKey(
                name: "FK_TeacherAvailabilitySlots_RecurringAvailabilityRules_Recurri~",
                table: "TeacherAvailabilitySlots",
                column: "RecurringAvailabilityRuleId",
                principalTable: "RecurringAvailabilityRules",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeacherAvailabilitySlots_RecurringAvailabilityRules_Recurri~",
                table: "TeacherAvailabilitySlots");

            migrationBuilder.DropTable(
                name: "RecurringAvailabilityRules");

            migrationBuilder.DropIndex(
                name: "IX_TeacherAvailabilitySlots_RecurringAvailabilityRuleId_Date",
                table: "TeacherAvailabilitySlots");

            migrationBuilder.DropColumn(
                name: "RecurringAvailabilityRuleId",
                table: "TeacherAvailabilitySlots");
        }
    }
}

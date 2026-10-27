using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTestInstanceRetakeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AttemptStartedAt",
                table: "TestInstances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstCompletedAt",
                table: "TestInstances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetakeCount",
                table: "TestInstances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // issue #609: mevcut tamamlanmış oturumların ilk tamamlanma anı = EndTime (Status 1 = Completed). Expired / Started dokunulmaz.
            migrationBuilder.Sql(
                "UPDATE \"TestInstances\" SET \"FirstCompletedAt\" = \"EndTime\" " +
                "WHERE \"Status\" = 1 AND \"EndTime\" IS NOT NULL AND \"FirstCompletedAt\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttemptStartedAt",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "FirstCompletedAt",
                table: "TestInstances");

            migrationBuilder.DropColumn(
                name: "RetakeCount",
                table: "TestInstances");
        }
    }
}

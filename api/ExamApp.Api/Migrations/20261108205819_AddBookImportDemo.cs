using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookImportDemo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DemoSourceJobId",
                table: "BookImportJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemoSource",
                table: "BookImportJobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_BookImportJobs_DemoSourceJobId",
                table: "BookImportJobs",
                column: "DemoSourceJobId");

            migrationBuilder.CreateIndex(
                name: "IX_BookImportJobs_IsDemoSource",
                table: "BookImportJobs",
                column: "IsDemoSource",
                unique: true,
                filter: "\"IsDemoSource\" AND NOT \"IsDeleted\"");

            migrationBuilder.AddForeignKey(
                name: "FK_BookImportJobs_BookImportJobs_DemoSourceJobId",
                table: "BookImportJobs",
                column: "DemoSourceJobId",
                principalTable: "BookImportJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookImportJobs_BookImportJobs_DemoSourceJobId",
                table: "BookImportJobs");

            migrationBuilder.DropIndex(
                name: "IX_BookImportJobs_DemoSourceJobId",
                table: "BookImportJobs");

            migrationBuilder.DropIndex(
                name: "IX_BookImportJobs_IsDemoSource",
                table: "BookImportJobs");

            migrationBuilder.DropColumn(
                name: "DemoSourceJobId",
                table: "BookImportJobs");

            migrationBuilder.DropColumn(
                name: "IsDemoSource",
                table: "BookImportJobs");
        }
    }
}

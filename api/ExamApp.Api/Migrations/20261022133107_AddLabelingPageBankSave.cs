using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelingPageBankSave : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BankQuestionCount",
                table: "LabelingPages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BankSaveStartedAt",
                table: "LabelingPages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BankSavedAt",
                table: "LabelingPages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankSavedByKeycloakId",
                table: "LabelingPages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankSavedByName",
                table: "LabelingPages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankQuestionCount",
                table: "LabelingPages");

            migrationBuilder.DropColumn(
                name: "BankSaveStartedAt",
                table: "LabelingPages");

            migrationBuilder.DropColumn(
                name: "BankSavedAt",
                table: "LabelingPages");

            migrationBuilder.DropColumn(
                name: "BankSavedByKeycloakId",
                table: "LabelingPages");

            migrationBuilder.DropColumn(
                name: "BankSavedByName",
                table: "LabelingPages");
        }
    }
}

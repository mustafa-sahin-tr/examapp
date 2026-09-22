using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolExternalCodeAndSeedFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalCode",
                table: "Schools",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSeedData",
                table: "Schools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Schools_ExternalCode",
                table: "Schools",
                column: "ExternalCode",
                unique: true,
                filter: "\"ExternalCode\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Schools_ExternalCode",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "ExternalCode",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "IsSeedData",
                table: "Schools");
        }
    }
}

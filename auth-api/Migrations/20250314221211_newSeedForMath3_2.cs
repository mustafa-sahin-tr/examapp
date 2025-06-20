using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class newSeedForMath3_2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Topics",
                columns: new[] { "Id", "GradeId", "Name", "SubjectId" },
                values: new object[] { 212, 3, "Tartma", 2 });

            migrationBuilder.InsertData(
                table: "SubTopics",
                columns: new[] { "Id", "Name", "TopicId" },
                values: new object[] { 231, "Tartma", 212 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 231);

            migrationBuilder.DeleteData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 212);
        }
    }
}

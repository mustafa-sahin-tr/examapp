using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class newSeedForMath3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Topics",
                columns: new[] { "Id", "GradeId", "Name", "SubjectId" },
                values: new object[] { 211, 3, "Paralarımız", 2 });

            migrationBuilder.InsertData(
                table: "SubTopics",
                columns: new[] { "Id", "Name", "TopicId" },
                values: new object[] { 230, "Paralarımız", 211 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 230);

            migrationBuilder.DeleteData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 211);
        }
    }
}

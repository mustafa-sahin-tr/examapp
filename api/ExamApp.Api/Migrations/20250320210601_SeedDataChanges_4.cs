using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedDataChanges_4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Topics",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "GradeId", "IsDeleted", "Name", "SubjectId", "UpdateTime", "UpdateUserId" },
                values: new object[] { 5, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, 3, false, "DOĞAL SAYILARLA BÖLME İŞLEMİ", 2, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Topics",
                keyColumn: "Id",
                keyValue: 5);
        }
    }
}

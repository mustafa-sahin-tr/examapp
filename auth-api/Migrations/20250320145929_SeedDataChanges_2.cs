using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedDataChanges_2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 2,
                column: "Name",
                value: "Birer, Onar, Yüzer Ritmik Sayma");

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 5,
                column: "Name",
                value: "Basamak Adları ve Değerleri");

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 8,
                column: "Name",
                value: "Doğal Sayıları En Yakın Onluğa ve Yüzlüğe Yuvarlama");

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 10,
                column: "Name",
                value: "Doğal Sayıları Sıralama ve Karşılaştırma");

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 12,
                column: "Name",
                value: "Altışar,Yedişer,Sekizer,Dokuzar  Ritmik Sayma");

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 20,
                column: "Name",
                value: "Eldeli ve Eldesiz Toplama İşlemi");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 2,
                column: "Name",
                value: "Birer Ritmik Sayma");

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 5,
                column: "Name",
                value: "Basamak Adları");

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 8,
                column: "Name",
                value: "Doğal Sayıları En Yakın Onluğa Yuvarlama");

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 10,
                column: "Name",
                value: "Doğal Sayıları Karşılaştırma");

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 12,
                column: "Name",
                value: "Altışar Ritmik Sayma");

            migrationBuilder.UpdateData(
                table: "SubTopics",
                keyColumn: "Id",
                keyValue: 20,
                column: "Name",
                value: "Eldesiz Toplama İşlemi");

            migrationBuilder.InsertData(
                table: "SubTopics",
                columns: new[] { "Id", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "Name", "TopicId", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 3, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Onar Ritmik Sayma", 1, null, null },
                    { 4, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Yüzer Ritmik Sayma", 1, null, null },
                    { 6, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Basamak Değerleri", 1, null, null },
                    { 9, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Doğal Sayıları En Yakın Yüzlüğe Yuvarlama", 1, null, null },
                    { 11, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Doğal Sayıları Sıralama", 1, null, null },
                    { 13, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Yedişer Ritmik Sayma", 1, null, null },
                    { 14, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Sekizer Ritmik Sayma", 1, null, null },
                    { 15, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Dokuzar Ritmik Sayma", 1, null, null },
                    { 18, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Toplamı Tek mi Çift mi?", 1, null, null },
                    { 21, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Eldeli Toplama İşlemi", 2, null, null },
                    { 24, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Çıkaralım Bulalım", 3, null, null },
                    { 26, new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, null, null, false, "Onluk Bozarak Çıkarma İşlemi", 3, null, null }
                });
        }
    }
}

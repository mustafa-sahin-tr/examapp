using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddContextBasedQuestionSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsContextBased",
                table: "Questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'1000', '1', '', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_Skills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuestionSkills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    SkillId = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_QuestionSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionSkills_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuestionSkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Skills",
                columns: new[] { "Id", "Category", "Code", "CreateTime", "CreateUserId", "DeleteTime", "DeleteUserId", "IsDeleted", "Name", "SortOrder", "UpdateTime", "UpdateUserId" },
                values: new object[,]
                {
                    { 1, 2, "KB2.1", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Çelişki giderme", 1, null, null },
                    { 2, 2, "KB2.2", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Gözlemleme", 2, null, null },
                    { 3, 2, "KB2.3", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Özetleme", 3, null, null },
                    { 4, 2, "KB2.4", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Çözümleme", 4, null, null },
                    { 5, 2, "KB2.5", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Sınıflandırma", 5, null, null },
                    { 6, 2, "KB2.6", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Bilgi toplama", 6, null, null },
                    { 7, 2, "KB2.7", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Karşılaştırma", 7, null, null },
                    { 8, 2, "KB2.8", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Sorgulama", 8, null, null },
                    { 9, 2, "KB2.9", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Genelleme", 9, null, null },
                    { 10, 2, "KB2.10", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Çıkarım yapma", 10, null, null },
                    { 11, 2, "KB2.11", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Gözleme dayalı tahmin etme", 11, null, null },
                    { 12, 2, "KB2.12", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Mevcut bilgiye/veriye dayalı tahmin etme", 12, null, null },
                    { 13, 2, "KB2.13", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Yapılandırma", 13, null, null },
                    { 14, 2, "KB2.14", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Yorumlama", 14, null, null },
                    { 15, 2, "KB2.15", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Yansıtma", 15, null, null },
                    { 16, 2, "KB2.16", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Muhakeme (akıl yürütme)", 16, null, null },
                    { 17, 2, "KB2.17", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Değerlendirme", 17, null, null },
                    { 18, 2, "KB2.18", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Tartışma", 18, null, null },
                    { 19, 2, "KB2.19", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Mantıksal denetleme", 19, null, null },
                    { 20, 2, "KB2.20", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Sentezleme", 20, null, null },
                    { 101, 3, "KB3.1", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Karar verme", 101, null, null },
                    { 102, 3, "KB3.2", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Problem çözme", 102, null, null },
                    { 103, 3, "KB3.3", new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, false, "Eleştirel düşünme", 103, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSkills_QuestionId_SkillId",
                table: "QuestionSkills",
                columns: new[] { "QuestionId", "SkillId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSkills_SkillId",
                table: "QuestionSkills",
                column: "SkillId");

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Code",
                table: "Skills",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionSkills");

            migrationBuilder.DropTable(
                name: "Skills");

            migrationBuilder.DropColumn(
                name: "IsContextBased",
                table: "Questions");
        }
    }
}

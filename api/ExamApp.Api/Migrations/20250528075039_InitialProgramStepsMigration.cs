using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ExamApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialProgramStepsMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Multiple",
                table: "ProgramSteps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ProgramStepActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    ProgramStepId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramStepActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgramStepActions_ProgramSteps_ProgramStepId",
                        column: x => x.ProgramStepId,
                        principalTable: "ProgramSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProgramStepOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Selected = table.Column<bool>(type: "boolean", nullable: true),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    NextStep = table.Column<int>(type: "integer", nullable: true),
                    ProgramStepId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramStepOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgramStepOptions_ProgramSteps_ProgramStepId",
                        column: x => x.ProgramStepId,
                        principalTable: "ProgramSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ProgramSteps",
                columns: new[] { "Id", "Description", "Multiple", "Order", "Title" },
                values: new object[,]
                {
                    { 1, "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin?", false, 1, "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin?" },
                    { 2, "Sana uygun olan çalışma süresini seçebilirsin.", false, 2, "Sana uygun olan çalışma süresini seçebilirsin." },
                    { 3, "Bir dersten bir günde kaç soru çözersin?", false, 3, "Bir dersten bir günde kaç soru çözersin?" },
                    { 4, "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin", false, 4, "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin" },
                    { 5, "Bir günde kaç farklı ders çalışmak istersin?", false, 5, "Bir günde kaç farklı ders çalışmak istersin?" },
                    { 6, "Ders çalışamayacağın gün var mı?", true, 6, "Ders çalışamayacağın gün var mı?" },
                    { 7, "Çalışırken zorlandığın ders / dersler hangileri?", true, 7, "Çalışırken zorlandığın ders / dersler hangileri?" },
                    { 8, "Artık programını oluşturmaya hazırsın", false, 8, "Artık programını oluşturmaya hazırsın" }
                });

            migrationBuilder.InsertData(
                table: "ProgramStepActions",
                columns: new[] { "Id", "Label", "ProgramStepId", "Value" },
                values: new object[,]
                {
                    { 1, "Programı Oluştur", 8, "CreateProgram" },
                    { 2, "Vazgeç", 8, "Cancel" }
                });

            migrationBuilder.InsertData(
                table: "ProgramStepOptions",
                columns: new[] { "Id", "Icon", "Label", "NextStep", "ProgramStepId", "Selected", "Value" },
                values: new object[,]
                {
                    { 1, "icons/question-mark.svg", "Süreli Çalışma", 2, 1, false, "time" },
                    { 2, "icons/timer.svg", "Soru Sayısı Takipli Çalışma", 3, 1, false, "question" },
                    { 3, "icons/question-mark.svg", "25 dakika çalışma 5 dakika ara", 5, 2, false, "25-5" },
                    { 4, "icons/question-mark.svg", "30 dakika çalışma 10 dakika ara", 5, 2, false, "30-10" },
                    { 5, "icons/question-mark.svg", "40 dakika çalışma 10 dakika ara", 5, 2, false, "40-10" },
                    { 6, "icons/question-mark.svg", "50 dakika çalışma 10 dakika ara", 5, 2, false, "50-10" },
                    { 7, "icons/question-mark.svg", "8", 5, 3, false, "8" },
                    { 8, "icons/question-mark.svg", "12", 5, 3, false, "12" },
                    { 9, "icons/question-mark.svg", "16", 5, 3, false, "16" },
                    { 10, "icons/question-mark.svg", "Süreli Çalışma", -1, 4, false, "time" },
                    { 11, "icons/question-mark.svg", "Soru Sayısı Takipli Çalışma", -1, 4, false, "question" },
                    { 12, "icons/one-svgrepo-com.svg", "1", 6, 5, false, "1" },
                    { 13, "icons/two-svgrepo-com.svg", "2", 6, 5, false, "2" },
                    { 14, "icons/three-svgrepo-com.svg", "3", 6, 5, false, "3" },
                    { 15, "icons/monday-svgrepo-com.svg", "Pazartesi", 7, 6, false, "1" },
                    { 16, "icons/tuesday-svgrepo-com.svg", "Salı", 7, 6, false, "2" },
                    { 17, "icons/wednesday-svgrepo-com.svg", "Çarşamba", 7, 6, false, "3" },
                    { 18, "icons/thursday-svgrepo-com.svg", "Perşembe", 7, 6, false, "4" },
                    { 19, "icons/friday-svgrepo-com.svg", "Cuma", 7, 6, false, "5" },
                    { 20, "icons/saturday-svgrepo-com.svg", "Cumartesi", 7, 6, false, "6" },
                    { 21, "icons/sunday-svgrepo-com.svg", "Pazar", 7, 6, false, "7" },
                    { 22, "icons/null-svgrepo-com.svg", "Yok", 7, 6, false, "8" },
                    { 23, "icons/home-svgrepo-com.svg", "Hayat Bilgisi", 8, 7, false, "1" },
                    { 24, "icons/alphabet-svgrepo-com.svg", "Türkçe", 8, 7, false, "2" },
                    { 25, "icons/math-svgrepo-com.svg", "Matematik", 8, 7, false, "3" },
                    { 26, "icons/world-svgrepo-com.svg", "Fen Bilimleri", 8, 7, false, "4" },
                    { 27, "icons/null-svgrepo-com.svg", "Yok", 8, 7, false, "5" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProgramStepActions_ProgramStepId",
                table: "ProgramStepActions",
                column: "ProgramStepId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramStepOptions_ProgramStepId",
                table: "ProgramStepOptions",
                column: "ProgramStepId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProgramStepActions");

            migrationBuilder.DropTable(
                name: "ProgramStepOptions");

            migrationBuilder.DeleteData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DropColumn(
                name: "Multiple",
                table: "ProgramSteps");
        }
    }
}

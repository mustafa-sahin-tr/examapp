using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <summary>
    /// Issue #136: the program wizard froze after step 7 because all of its options pointed to
    /// NextStep = 8, which never existed. Step 7 is now the terminal step (NextStep = null).
    /// Step 4 was an unreachable duplicate of step 1 and is removed together with its options.
    ///
    /// ProgramStepSeed.SeedData is not wired into the model (HasData is not active), so EF cannot
    /// diff this seed data automatically; the data operations below are written by hand.
    /// </summary>
    public partial class FixProgramStepWizardDeadEnd : Migration
    {
        private static readonly int[] Step7OptionIds = { 23, 24, 25, 26, 27 };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 7 becomes the last step of the wizard.
            foreach (var optionId in Step7OptionIds)
            {
                migrationBuilder.UpdateData(
                    table: "ProgramStepOptions",
                    keyColumn: "Id",
                    keyValue: optionId,
                    column: "NextStep",
                    value: null);
            }

            // Remove the dead step 4 (children first, then the parent).
            migrationBuilder.DeleteData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "ProgramStepOptions",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "ProgramSteps",
                keyColumn: "Id",
                keyValue: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ProgramSteps",
                columns: new[] { "Id", "Title", "Description", "Order", "Multiple", "IsDeleted" },
                values: new object[]
                {
                    4,
                    "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin",
                    "Süreli mi yoksa soru sayısı takipli bir çalışma mı planlamak istersin",
                    4,
                    false,
                    false
                });

            migrationBuilder.InsertData(
                table: "ProgramStepOptions",
                columns: new[] { "Id", "ProgramStepId", "Label", "Value", "Selected", "Icon", "NextStep", "IsDeleted" },
                values: new object[,]
                {
                    { 10, 4, "Süreli Çalışma", "time", false, "icons/question-mark.svg", -1, false },
                    { 11, 4, "Soru Sayısı Takipli Çalışma", "question", false, "icons/question-mark.svg", -1, false }
                });

            foreach (var optionId in Step7OptionIds)
            {
                migrationBuilder.UpdateData(
                    table: "ProgramStepOptions",
                    keyColumn: "Id",
                    keyValue: optionId,
                    column: "NextStep",
                    value: 8);
            }
        }
    }
}

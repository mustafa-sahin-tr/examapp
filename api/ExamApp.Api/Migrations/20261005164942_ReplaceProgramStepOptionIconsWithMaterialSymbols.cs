using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamApp.Api.Migrations
{
    /// <summary>
    /// Issue #319: <c>ProgramStepOptions.Icon</c> now stores a Material Symbols ligature name
    /// (rendered by the UI as <c>&lt;mat-icon&gt;{{ opt.icon }}&lt;/mat-icon&gt;</c>) instead of an
    /// <c>icons/*.svg</c> path. Mapping comes from the #135 mockup (issue #319 comment).
    ///
    /// ProgramStepSeed.SeedData is not wired into the model (HasData is not active), so EF cannot
    /// diff this seed data automatically; the data operations below are written by hand, keyed by
    /// the option Ids inserted in InitialProgramStepsMigration (10-11 were removed in
    /// FixProgramStepWizardDeadEnd).
    /// </summary>
    public partial class ReplaceProgramStepOptionIconsWithMaterialSymbols : Migration
    {
        // OptionId, new Material Symbols name, previous svg path (for Down).
        private static readonly (int Id, string Icon, string OldIcon)[] Icons =
        {
            // Step 1
            (1, "timer", "icons/question-mark.svg"),
            (2, "quiz", "icons/timer.svg"),
            // Step 2
            (3, "hourglass_empty", "icons/question-mark.svg"),
            (4, "hourglass_top", "icons/question-mark.svg"),
            (5, "hourglass_bottom", "icons/question-mark.svg"),
            (6, "hourglass_full", "icons/question-mark.svg"),
            // Step 3
            (7, "note", "icons/question-mark.svg"),
            (8, "library_books", "icons/question-mark.svg"),
            (9, "auto_stories", "icons/question-mark.svg"),
            // Step 5
            (12, "filter_1", "icons/one-svgrepo-com.svg"),
            (13, "filter_2", "icons/two-svgrepo-com.svg"),
            (14, "filter_3", "icons/three-svgrepo-com.svg"),
            // Step 6
            (15, "looks_one", "icons/monday-svgrepo-com.svg"),
            (16, "looks_two", "icons/tuesday-svgrepo-com.svg"),
            (17, "looks_3", "icons/wednesday-svgrepo-com.svg"),
            (18, "looks_4", "icons/thursday-svgrepo-com.svg"),
            (19, "looks_5", "icons/friday-svgrepo-com.svg"),
            (20, "weekend", "icons/saturday-svgrepo-com.svg"),
            (21, "wb_sunny", "icons/sunday-svgrepo-com.svg"),
            (22, "event_available", "icons/null-svgrepo-com.svg"),
            // Step 7
            (23, "home", "icons/home-svgrepo-com.svg"),
            (24, "spellcheck", "icons/alphabet-svgrepo-com.svg"),
            (25, "calculate", "icons/math-svgrepo-com.svg"),
            (26, "science", "icons/world-svgrepo-com.svg"),
            (27, "sentiment_satisfied", "icons/null-svgrepo-com.svg"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (id, icon, _) in Icons)
            {
                migrationBuilder.UpdateData(
                    table: "ProgramStepOptions",
                    keyColumn: "Id",
                    keyValue: id,
                    column: "Icon",
                    value: icon);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (id, _, oldIcon) in Icons)
            {
                migrationBuilder.UpdateData(
                    table: "ProgramStepOptions",
                    keyColumn: "Id",
                    keyValue: id,
                    column: "Icon",
                    value: oldIcon);
            }
        }
    }
}

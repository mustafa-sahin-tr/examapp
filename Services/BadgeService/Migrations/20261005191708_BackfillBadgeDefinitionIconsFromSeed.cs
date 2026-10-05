using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BadgeService.Migrations
{
    /// <inheritdoc />
    public partial class BackfillBadgeDefinitionIconsFromSeed : Migration
    {
        // Issue #149 (data migration, hand-written SQL per ef-migration skill exception): one-time backfill of
        // the new "Icon" column for the 37 seeded badges (Code -> Material Symbols name, issue #149 icon table).
        // A row is only touched if it still carries its ORIGINAL seed IconUrl and has no Icon yet — a badge an
        // admin edited (different/cleared IconUrl) or already gave an Icon is left alone. Must stay in sync with
        // BadgeSeeder (pinned by BadgeIconBackfillMigrationTests.Backfill_sets_the_seeder_icon_on_every_untouched_seed_row).
        private const string IconMap = @"
                (VALUES
                    ('first-answer', 'achievements/disabled-dark.0085b3.svg', 'flag'),
                    ('correct-streak-5', 'achievements/disabled-dark.041736.svg', 'done_all'),
                    ('question-hunter-1', 'achievements/disabled-dark.0b4480.svg', 'gps_fixed'),
                    ('question-hunter-2', 'achievements/disabled-dark.148553.svg', 'gps_fixed'),
                    ('question-hunter-3', 'achievements/disabled-dark.16380c.svg', 'gps_fixed'),
                    ('question-hunter-4', 'achievements/disabled-dark.1679e1.svg', 'gps_fixed'),
                    ('question-hunter-5', 'achievements/disabled-dark.1e1b53.svg', 'gps_fixed'),
                    ('accuracy-journey-1', 'achievements/disabled-dark.21b1cf.svg', 'task_alt'),
                    ('accuracy-journey-2', 'achievements/disabled-dark.0085b3.svg', 'task_alt'),
                    ('accuracy-journey-3', 'achievements/disabled-dark.041736.svg', 'task_alt'),
                    ('study-time-1', 'achievements/disabled-dark.0085b3.svg', 'rocket_launch'),
                    ('study-time-2', 'achievements/disabled-dark.041736.svg', 'theater_comedy'),
                    ('study-time-3', 'achievements/disabled-dark.0b4480.svg', 'psychology'),
                    ('study-time-4', 'achievements/disabled-dark.148553.svg', 'schedule'),
                    ('study-time-5', 'achievements/disabled-dark.16380c.svg', 'visibility'),
                    ('study-time-6', 'achievements/disabled-dark.1679e1.svg', 'travel_explore'),
                    ('study-time-7', 'achievements/disabled-dark.1e1b53.svg', 'school'),
                    ('study-time-8', 'achievements/disabled-dark.21b1cf.svg', 'workspace_premium'),
                    ('subject-turkce-mastery', 'achievements/disabled-dark.0085b3.svg', 'menu_book'),
                    ('subject-turkce-expert', 'achievements/disabled-dark.041736.svg', 'verified'),
                    ('subject-turkce-time', 'achievements/disabled-dark.0b4480.svg', 'timer'),
                    ('subject-matematik-mastery', 'achievements/disabled-dark.0085b3.svg', 'calculate'),
                    ('subject-matematik-expert', 'achievements/disabled-dark.041736.svg', 'verified'),
                    ('subject-matematik-time', 'achievements/disabled-dark.0b4480.svg', 'timer'),
                    ('subject-fen-bilimleri-mastery', 'achievements/disabled-dark.0085b3.svg', 'science'),
                    ('subject-fen-bilimleri-expert', 'achievements/disabled-dark.041736.svg', 'verified'),
                    ('subject-fen-bilimleri-time', 'achievements/disabled-dark.0b4480.svg', 'timer'),
                    ('subject-sosyal-bilgiler-mastery', 'achievements/disabled-dark.0085b3.svg', 'public'),
                    ('subject-sosyal-bilgiler-expert', 'achievements/disabled-dark.041736.svg', 'verified'),
                    ('subject-sosyal-bilgiler-time', 'achievements/disabled-dark.0b4480.svg', 'timer'),
                    ('streak-1', 'achievements/disabled-dark.148553.svg', 'local_fire_department'),
                    ('streak-2', 'achievements/disabled-dark.16380c.svg', 'local_fire_department'),
                    ('streak-3', 'achievements/disabled-dark.1679e1.svg', 'local_fire_department'),
                    ('streak-4', 'achievements/disabled-dark.1e1b53.svg', 'local_fire_department'),
                    ('active-days-1', 'achievements/disabled-dark.21b1cf.svg', 'event_available'),
                    ('active-days-2', 'achievements/disabled-dark.0085b3.svg', 'event_available'),
                    ('active-days-3', 'achievements/disabled-dark.041736.svg', 'event_available')
                ) AS m(code, icon_url, icon)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""BadgeDefinitions"" AS b
                SET ""Icon"" = m.icon
                FROM " + IconMap + @"
                WHERE b.""Code"" = m.code
                  AND b.""IconUrl"" = m.icon_url
                  AND b.""Icon"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverts only what Up() could have written (same Code + seed IconUrl + the mapped icon).
            migrationBuilder.Sql(@"
                UPDATE ""BadgeDefinitions"" AS b
                SET ""Icon"" = NULL
                FROM " + IconMap + @"
                WHERE b.""Code"" = m.code
                  AND b.""IconUrl"" = m.icon_url
                  AND b.""Icon"" = m.icon;");
        }
    }
}

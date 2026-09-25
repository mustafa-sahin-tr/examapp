using BadgeService;
using BadgeService.Data;
using BadgeService.Entities;
using BadgeService.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Tests;

/// <summary>
/// Issue #148 (owner decision #3): the seeder only INSERTS badges missing by <c>Code</c>; it must never
/// touch a row whose Code already exists, whether that row was created by a previous seed run or edited
/// by an admin via <see cref="BadgeService.Services.BadgeDefinitionAdminService"/>.
/// </summary>
public class BadgeSeederTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    [Fact]
    public async Task SeedAsync_inserts_known_badges_into_an_empty_database()
    {
        await using (var ctx = _db.NewContext())
        {
            await BadgeSeeder.SeedAsync(ctx);
        }

        await using var read = _db.NewContext();
        var count = await read.BadgeDefinitions.CountAsync();
        count.ShouldBeGreaterThan(0);

        var firstAnswer = await read.BadgeDefinitions.FirstOrDefaultAsync(x => x.Code == "first-answer");
        firstAnswer.ShouldNotBeNull();
        firstAnswer!.IsActive.ShouldBeTrue();
        firstAnswer.CreatedBy.ShouldBe("system-seed");
    }

    [Fact]
    public async Task SeedAsync_never_overwrites_an_admin_edited_row_with_a_known_code()
    {
        await using (var ctx = _db.NewContext())
        {
            // Simulate a row an admin already edited via the CRUD API — same Code the seeder would use,
            // but every other field deliberately different from what BadgeSeeder would write.
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(),
                Code = "first-answer",
                Name = "Admin'in Değiştirdiği İsim",
                Description = "Admin açıklaması",
                Category = "ÖzelKategori",
                RuleType = "AnswerCount",
                RuleConfigJson = "{\"target\":999}",
                IconUrl = "achievements/custom.svg",
                IsActive = false,
                CreatedBy = "admin-1",
                CreatedAtUtc = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            await BadgeSeeder.SeedAsync(ctx);
        }

        await using var read = _db.NewContext();
        var row = await read.BadgeDefinitions.SingleAsync(x => x.Code == "first-answer");

        row.Name.ShouldBe("Admin'in Değiştirdiği İsim");
        row.RuleConfigJson.ShouldBe("{\"target\":999}");
        row.IsActive.ShouldBeFalse();
        row.CreatedBy.ShouldBe("admin-1");
    }

    [Fact]
    public async Task SeedAsync_is_idempotent_across_repeated_runs()
    {
        await using (var ctx = _db.NewContext())
        {
            await BadgeSeeder.SeedAsync(ctx);
        }

        int countAfterFirstRun;
        await using (var ctx = _db.NewContext())
        {
            countAfterFirstRun = await ctx.BadgeDefinitions.CountAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            await BadgeSeeder.SeedAsync(ctx);
        }

        await using var read = _db.NewContext();
        (await read.BadgeDefinitions.CountAsync()).ShouldBe(countAfterFirstRun);
    }

    [Fact]
    public async Task SeedAsync_produces_only_canonical_target_based_rule_configs()
    {
        // Code review follow-up (#148, SHOULD-FIX): the migration normalizes legacy rule-config keys
        // (count/streak/days/targetMinutes/minutes) to "target" for existing rows — the seeder's own
        // (potentially newly-inserted) configs must already be canonical, never re-introduce a legacy key.
        await using (var ctx = _db.NewContext())
        {
            await BadgeSeeder.SeedAsync(ctx);
        }

        await using var read = _db.NewContext();
        var all = await read.BadgeDefinitions.ToListAsync();

        all.ShouldNotBeEmpty();
        foreach (var badge in all)
        {
            using var document = System.Text.Json.JsonDocument.Parse(badge.RuleConfigJson);
            document.RootElement.TryGetProperty("target", out _).ShouldBeTrue($"{badge.Code} RuleConfigJson'da 'target' alanı yok: {badge.RuleConfigJson}");

            foreach (var legacyKey in new[] { "count", "streak", "days", "targetMinutes", "minutes" })
            {
                document.RootElement.TryGetProperty(legacyKey, out _).ShouldBeFalse(
                    $"{badge.Code} RuleConfigJson'da eski anahtar '{legacyKey}' hâlâ var: {badge.RuleConfigJson}");
            }
        }
    }

    /// <summary>
    /// Code review follow-up (#148, SHOULD-FIX): the migration's hand-written Name→Code backfill SQL
    /// (<c>AddBadgeDefinitionCodeAndAuditFields</c>) is data, not something this test can execute (it's
    /// Postgres-specific jsonb/SQL run at migrate time, and this suite is Sqlite-only) — but it MUST stay
    /// in sync with what <see cref="BadgeSeeder"/> currently produces, or a fresh DB and a
    /// migrated-from-before-#148 DB would disagree on some badge's Code. This mirrors that SQL's mapping
    /// and fails loudly if the two ever drift apart, in either direction.
    /// </summary>
    [Fact]
    public void Migration_name_to_code_backfill_mapping_covers_every_current_seeder_definition()
    {
        var migrationNameToCode = new Dictionary<string, string>
        {
            ["İlk Cevap"] = "first-answer",
            ["5 Doğru Üst Üste"] = "correct-streak-5",
            ["Soru Avcısı I"] = "question-hunter-1",
            ["Soru Avcısı II"] = "question-hunter-2",
            ["Soru Avcısı III"] = "question-hunter-3",
            ["Soru Avcısı IV"] = "question-hunter-4",
            ["Soru Avcısı V"] = "question-hunter-5",
            ["Doğru Yolu Bul I"] = "accuracy-journey-1",
            ["Doğru Yolu Bul II"] = "accuracy-journey-2",
            ["Doğru Yolu Bul III"] = "accuracy-journey-3",
            ["Hızlı Başlangıç"] = "study-time-1",
            ["Show Time"] = "study-time-2",
            ["Bilgi Avcısı"] = "study-time-3",
            ["Prime Time"] = "study-time-4",
            ["Bilge İzleyici"] = "study-time-5",
            ["Zaman Yolcusu"] = "study-time-6",
            ["Akademik Yolculuk"] = "study-time-7",
            ["Elit Çalışkan"] = "study-time-8",
            ["Türkçe Ustası"] = "subject-turkce-mastery",
            ["Türkçe Uzmanı"] = "subject-turkce-expert",
            ["Türkçe Zaman Ustası"] = "subject-turkce-time",
            ["Matematik Ustası"] = "subject-matematik-mastery",
            ["Matematik Uzmanı"] = "subject-matematik-expert",
            ["Matematik Zaman Ustası"] = "subject-matematik-time",
            ["Fen Bilimleri Ustası"] = "subject-fen-bilimleri-mastery",
            ["Fen Bilimleri Uzmanı"] = "subject-fen-bilimleri-expert",
            ["Fen Bilimleri Zaman Ustası"] = "subject-fen-bilimleri-time",
            ["Sosyal Bilgiler Ustası"] = "subject-sosyal-bilgiler-mastery",
            ["Sosyal Bilgiler Uzmanı"] = "subject-sosyal-bilgiler-expert",
            ["Sosyal Bilgiler Zaman Ustası"] = "subject-sosyal-bilgiler-time",
            ["İstikrarlı Öğrenci I"] = "streak-1",
            ["İstikrarlı Öğrenci II"] = "streak-2",
            ["İstikrarlı Öğrenci III"] = "streak-3",
            ["İstikrarlı Öğrenci IV"] = "streak-4",
            ["Yeni Alışkanlıklar"] = "active-days-1",
            ["Alışkanlık Sahibi"] = "active-days-2",
            ["Sürekli Öğrenen"] = "active-days-3",
        };

        var desired = BadgeSeeder.GetDesiredNameCodePairsForTesting();

        desired.Count.ShouldBe(migrationNameToCode.Count);
        foreach (var (name, code) in desired)
        {
            migrationNameToCode.ShouldContainKey(name);
            migrationNameToCode[name].ShouldBe(code);
        }
    }

    public void Dispose() => _db.Dispose();
}

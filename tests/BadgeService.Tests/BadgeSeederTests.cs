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

    public void Dispose() => _db.Dispose();
}

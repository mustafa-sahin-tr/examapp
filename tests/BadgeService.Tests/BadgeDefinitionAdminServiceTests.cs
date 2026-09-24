using BadgeService;
using BadgeService.Entities;
using BadgeService.Models;
using BadgeService.Services;
using BadgeService.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Tests;

/// <summary>
/// Issue #148: CRUD + validation for <see cref="BadgeDefinitionAdminService"/>, and the
/// deactivate/evaluate/hide-from-catalog semantics that fall out of <c>IsActive</c>.
/// </summary>
public class BadgeDefinitionAdminServiceTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    [Fact]
    public async Task CreateAsync_persists_a_valid_badge_and_stamps_audit_fields()
    {
        await using var ctx = _db.NewContext();
        var service = new BadgeDefinitionAdminService(ctx);

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "new-badge",
            Name = "Yeni Rozet",
            Description = "Açıklama",
            Category = "Test",
            IconUrl = "achievements/disabled-dark.0085b3.svg",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":5}",
        }, actor: "admin-1", CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.Value!.Code.ShouldBe("new-badge");
        result.Value.IsActive.ShouldBeTrue();
        result.Value.CreatedBy.ShouldBe("admin-1");
        result.Value.RuleConfigJson.ShouldBe("{\"target\":5}");

        (await ctx.BadgeDefinitions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_code()
    {
        await using var ctx = _db.NewContext();
        var service = new BadgeDefinitionAdminService(ctx);
        var request = new CreateBadgeDefinitionRequest
        {
            Code = "dup",
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        };

        (await service.CreateAsync(request, "admin", CancellationToken.None)).Succeeded.ShouldBeTrue();
        var duplicate = new CreateBadgeDefinitionRequest
        {
            Code = "dup",
            Name = "B",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        };
        var second = await service.CreateAsync(duplicate, "admin", CancellationToken.None);

        second.IsConflict.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateAsync_rejects_invalid_rule_config()
    {
        await using var ctx = _db.NewContext();
        var service = new BadgeDefinitionAdminService(ctx);

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "bad-rule",
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{}", // missing target
        }, "admin", CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Field == "target");
    }

    [Fact]
    public async Task CreateAsync_rejects_invalid_icon_url()
    {
        await using var ctx = _db.NewContext();
        var service = new BadgeDefinitionAdminService(ctx);

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "bad-icon",
            Name = "A",
            Category = "c",
            IconUrl = "https://evil.example.com/x.svg",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Field == "iconUrl");
    }

    [Fact]
    public async Task UpdateAsync_changes_the_rule_without_touching_already_earned_badges()
    {
        await using var ctx = _db.NewContext();
        var service = new BadgeDefinitionAdminService(ctx);

        var created = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "editable",
            Name = "Eski Ad",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":5}",
        }, "admin", CancellationToken.None);

        var earned = new BadgeEarned { Id = Guid.NewGuid(), UserId = 1, BadgeDefinitionId = created.Value!.Id, EarnedDate = DateTime.UtcNow };
        ctx.BadgeEarned.Add(earned);
        await ctx.SaveChangesAsync();

        var updated = await service.UpdateAsync(created.Value.Id, new UpdateBadgeDefinitionRequest
        {
            Name = "Yeni Ad",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":50}",
        }, "admin-2", CancellationToken.None);

        updated.Succeeded.ShouldBeTrue();
        updated.Value!.Name.ShouldBe("Yeni Ad");
        updated.Value.RuleConfigJson.ShouldBe("{\"target\":50}");
        updated.Value.UpdatedBy.ShouldBe("admin-2");

        // The already-awarded row is untouched by editing the rule.
        (await ctx.BadgeEarned.CountAsync(x => x.Id == earned.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task UpdateAsync_returns_not_found_for_unknown_id()
    {
        await using var ctx = _db.NewContext();
        var service = new BadgeDefinitionAdminService(ctx);

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateBadgeDefinitionRequest
        {
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", CancellationToken.None);

        result.IsNotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task SetActiveAsync_is_idempotent()
    {
        await using var ctx = _db.NewContext();
        var service = new BadgeDefinitionAdminService(ctx);

        var created = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "toggle",
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", CancellationToken.None);

        var deactivated1 = await service.SetActiveAsync(created.Value!.Id, isActive: false, "admin", CancellationToken.None);
        var deactivated2 = await service.SetActiveAsync(created.Value.Id, isActive: false, "admin", CancellationToken.None);

        deactivated1.Value!.IsActive.ShouldBeFalse();
        deactivated2.Value!.IsActive.ShouldBeFalse();

        var reactivated = await service.SetActiveAsync(created.Value.Id, isActive: true, "admin", CancellationToken.None);
        reactivated.Value!.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task ListAsync_excludes_inactive_by_default()
    {
        await using var ctx = _db.NewContext();
        var service = new BadgeDefinitionAdminService(ctx);

        var active = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "active-one", Name = "Active", Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}",
        }, "admin", CancellationToken.None);
        var toDeactivate = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "inactive-one", Name = "Inactive", Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}",
        }, "admin", CancellationToken.None);
        await service.SetActiveAsync(toDeactivate.Value!.Id, isActive: false, "admin", CancellationToken.None);

        var defaultList = await service.ListAsync(includeInactive: false, CancellationToken.None);
        defaultList.ShouldContain(x => x.Id == active.Value!.Id);
        defaultList.ShouldNotContain(x => x.Id == toDeactivate.Value.Id);

        var fullList = await service.ListAsync(includeInactive: true, CancellationToken.None);
        fullList.Count.ShouldBe(2);
    }

    public void Dispose() => _db.Dispose();
}

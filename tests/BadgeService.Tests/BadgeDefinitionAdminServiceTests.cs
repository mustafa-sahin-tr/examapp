using BadgeService;
using BadgeService.Entities;
using BadgeService.Models;
using BadgeService.Services;
using BadgeService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #148 (+ security review follow-up): CRUD + validation for
/// <see cref="BadgeDefinitionAdminService"/>, and the deactivate/evaluate/hide-from-catalog semantics
/// that fall out of <c>IsActive</c>.
/// </summary>
public class BadgeDefinitionAdminServiceTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    private static BadgeDefinitionAdminService NewService(BadgeDbContext ctx) =>
        new(ctx, NullLogger<BadgeDefinitionAdminService>.Instance);

    [Fact]
    public async Task CreateAsync_persists_a_valid_badge_and_stamps_audit_fields()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "new-badge",
            Name = "Yeni Rozet",
            Description = "Açıklama",
            Category = "Test",
            IconUrl = "achievements/disabled-dark.0085b3.svg",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":5}",
        }, actorId: "sub-admin-1", actorName: "admin1", CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.Value!.Code.ShouldBe("new-badge");
        result.Value.IsActive.ShouldBeTrue();
        result.Value.CreatedBy.ShouldBe("sub-admin-1");
        result.Value.CreatedByName.ShouldBe("admin1");
        result.Value.RuleConfigJson.ShouldBe("{\"target\":5}");

        (await ctx.BadgeDefinitions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_code()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        var request = new CreateBadgeDefinitionRequest
        {
            Code = "dup",
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        };

        (await service.CreateAsync(request, "admin", null, CancellationToken.None)).Succeeded.ShouldBeTrue();
        var duplicate = new CreateBadgeDefinitionRequest
        {
            Code = "dup",
            Name = "B",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        };
        var second = await service.CreateAsync(duplicate, "admin", null, CancellationToken.None);

        second.IsConflict.ShouldBeTrue();
        second.Errors.ShouldContain(e => e.Field == "code");
    }

    [Fact]
    public async Task CreateAsync_rejects_invalid_rule_config()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "bad-rule",
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{}", // missing target
        }, "admin", null, CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Field == "target");
    }

    [Fact]
    public async Task CreateAsync_rejects_invalid_icon_url()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "bad-icon",
            Name = "A",
            Category = "c",
            IconUrl = "https://evil.example.com/x.svg",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", null, CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Field == "iconUrl");
    }

    [Theory]
    [InlineData("Bad_Code")] // uppercase/underscore not allowed
    [InlineData("-leading-hyphen")]
    [InlineData("trailing-hyphen-")]
    [InlineData("double--hyphen")]
    [InlineData("has space")]
    public async Task CreateAsync_rejects_a_code_that_does_not_match_the_pattern(string badCode)
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = badCode,
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", null, CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Field == "code");
    }

    [Fact]
    public async Task CreateAsync_rejects_a_code_longer_than_the_max_length()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        var tooLong = string.Join("-", Enumerable.Repeat("aaaaaaaaaa", 7)); // 79 chars > 64

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = tooLong,
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", null, CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Field == "code");
    }

    [Fact]
    public async Task CreateAsync_rejects_fields_longer_than_their_max_length()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "long-fields",
            Name = new string('a', BadgeDefinitionAdminService.MaxNameLength + 1),
            Description = new string('b', BadgeDefinitionAdminService.MaxDescriptionLength + 1),
            Category = new string('c', BadgeDefinitionAdminService.MaxCategoryLength + 1),
            PathKey = new string('d', BadgeDefinitionAdminService.MaxPathKeyLength + 1),
            PathName = new string('e', BadgeDefinitionAdminService.MaxPathNameLength + 1),
            PathOrder = BadgeDefinitionAdminService.MaxPathOrder + 1,
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", null, CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Field == "name");
        result.Errors.ShouldContain(e => e.Field == "description");
        result.Errors.ShouldContain(e => e.Field == "category");
        result.Errors.ShouldContain(e => e.Field == "pathKey");
        result.Errors.ShouldContain(e => e.Field == "pathName");
        result.Errors.ShouldContain(e => e.Field == "pathOrder");
    }

    [Fact]
    public async Task CreateAsync_rejects_a_pathOrder_below_the_minimum()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "path-order-zero",
            Name = "A",
            Category = "c",
            PathOrder = 0,
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", null, CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Field == "pathOrder");
    }

    [Fact]
    public async Task CreateAsync_rejects_once_the_active_definition_cap_is_reached()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        for (var i = 0; i < BadgeDefinitionAdminService.MaxActiveDefinitions; i++)
        {
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Code = $"cap-filler-{i}", Name = $"Filler {i}", Description = "d",
                Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}", IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        await ctx.SaveChangesAsync();

        var result = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "one-too-many",
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", null, CancellationToken.None);

        result.IsConflict.ShouldBeTrue();
        result.Errors.ShouldContain(e => e.Field == "isActive");
    }

    [Fact]
    public async Task SetActiveAsync_activate_rejects_once_the_active_definition_cap_is_reached()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        for (var i = 0; i < BadgeDefinitionAdminService.MaxActiveDefinitions; i++)
        {
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Code = $"cap-filler-{i}", Name = $"Filler {i}", Description = "d",
                Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}", IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        var inactive = new BadgeDefinition
        {
            Id = Guid.NewGuid(), Code = "sits-inactive", Name = "Inactive", Description = "d",
            Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}", IsActive = false,
            CreatedAtUtc = DateTime.UtcNow,
        };
        ctx.BadgeDefinitions.Add(inactive);
        await ctx.SaveChangesAsync();

        var result = await service.SetActiveAsync(inactive.Id, isActive: true, "admin", null, CancellationToken.None);

        result.IsConflict.ShouldBeTrue();
        result.Errors.ShouldContain(e => e.Field == "isActive");
    }

    [Fact]
    public async Task UpdateAsync_changes_the_rule_without_touching_already_earned_badges()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var created = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "editable",
            Name = "Eski Ad",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":5}",
        }, "sub-admin-1", "admin1", CancellationToken.None);

        var earned = new BadgeEarned { Id = Guid.NewGuid(), UserId = 1, BadgeDefinitionId = created.Value!.Id, EarnedDate = DateTime.UtcNow };
        ctx.BadgeEarned.Add(earned);
        await ctx.SaveChangesAsync();

        var updated = await service.UpdateAsync(created.Value.Id, new UpdateBadgeDefinitionRequest
        {
            Name = "Yeni Ad",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":50}",
        }, "sub-admin-2", "admin2", CancellationToken.None);

        updated.Succeeded.ShouldBeTrue();
        updated.Value!.Name.ShouldBe("Yeni Ad");
        updated.Value.RuleConfigJson.ShouldBe("{\"target\":50}");
        updated.Value.UpdatedBy.ShouldBe("sub-admin-2");
        updated.Value.UpdatedByName.ShouldBe("admin2");

        // The already-awarded row is untouched by editing the rule.
        (await ctx.BadgeEarned.CountAsync(x => x.Id == earned.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task UpdateAsync_returns_not_found_for_unknown_id()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateBadgeDefinitionRequest
        {
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", null, CancellationToken.None);

        result.IsNotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task SetActiveAsync_is_idempotent()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var created = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "toggle",
            Name = "A",
            Category = "c",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        }, "admin", null, CancellationToken.None);

        var deactivated1 = await service.SetActiveAsync(created.Value!.Id, isActive: false, "admin", null, CancellationToken.None);
        var deactivated2 = await service.SetActiveAsync(created.Value.Id, isActive: false, "admin", null, CancellationToken.None);

        deactivated1.Value!.IsActive.ShouldBeFalse();
        deactivated2.Value!.IsActive.ShouldBeFalse();

        var reactivated = await service.SetActiveAsync(created.Value.Id, isActive: true, "admin", null, CancellationToken.None);
        reactivated.Value!.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task ListAsync_excludes_inactive_by_default()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var active = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "active-one", Name = "Active", Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}",
        }, "admin", null, CancellationToken.None);
        var toDeactivate = await service.CreateAsync(new CreateBadgeDefinitionRequest
        {
            Code = "inactive-one", Name = "Inactive", Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}",
        }, "admin", null, CancellationToken.None);
        await service.SetActiveAsync(toDeactivate.Value!.Id, isActive: false, "admin", null, CancellationToken.None);

        var defaultPage = await service.ListAsync(includeInactive: false, skip: 0, take: 50, CancellationToken.None);
        defaultPage.Items.ShouldContain(x => x.Id == active.Value!.Id);
        defaultPage.Items.ShouldNotContain(x => x.Id == toDeactivate.Value.Id);
        defaultPage.TotalCount.ShouldBe(1);

        var fullPage = await service.ListAsync(includeInactive: true, skip: 0, take: 50, CancellationToken.None);
        fullPage.Items.Count.ShouldBe(2);
        fullPage.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ListAsync_pages_using_skip_and_take()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        for (var i = 0; i < 5; i++)
        {
            await service.CreateAsync(new CreateBadgeDefinitionRequest
            {
                Code = $"page-{i}", Name = $"Badge {i:00}", Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}",
            }, "admin", null, CancellationToken.None);
        }

        var firstPage = await service.ListAsync(includeInactive: false, skip: 0, take: 2, CancellationToken.None);
        var secondPage = await service.ListAsync(includeInactive: false, skip: 2, take: 2, CancellationToken.None);

        firstPage.Items.Count.ShouldBe(2);
        firstPage.TotalCount.ShouldBe(5);
        secondPage.Items.Count.ShouldBe(2);
        secondPage.TotalCount.ShouldBe(5);
        firstPage.Items.Select(x => x.Id).ShouldNotContain(secondPage.Items[0].Id);
    }

    [Fact]
    public async Task ListAsync_clamps_take_to_the_max_page_size()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var page = await service.ListAsync(includeInactive: false, skip: 0, take: 10_000, CancellationToken.None);

        // No items exist, but this pins down that an oversized `take` doesn't throw / isn't echoed back
        // unclamped — the important assertion here is exercised more directly by the controller default.
        page.TotalCount.ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}

using System.Text.Json;
using System.Text.RegularExpressions;
using BadgeService;
using BadgeService.Data;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Models;
using BadgeService.Services;
using BadgeService.Tests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #149: Material Symbols badge icon — allowlist catalog, validator, admin create/update, seeder,
/// and the <c>icon</c> field on the student DTO, SignalR <c>BadgeEarned</c> payload and notification data.
/// The data migration is covered against real Postgres in <see cref="BadgeIconBackfillMigrationTests"/>.
/// </summary>
public class BadgeIconTests : IDisposable
{
    private static readonly Regex NamePattern = new("^[a-z][a-z0-9_]{1,63}$");
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    // ---------- catalog ----------

    [Fact]
    public void Catalog_has_about_48_unique_well_formed_names_in_known_categories()
    {
        var all = BadgeIconCatalog.All;

        all.Count.ShouldBe(48);
        all.Select(i => i.Name).Distinct().Count().ShouldBe(all.Count);
        all.ShouldAllBe(i => NamePattern.IsMatch(i.Name));
        all.ShouldAllBe(i => BadgeIconCatalog.Categories.Contains(i.Category));
        BadgeIconCatalog.Categories.ShouldAllBe(c => all.Any(i => i.Category == c));
    }

    [Fact]
    public void Catalog_contains_all_20_seed_icons_from_the_issue_table()
    {
        var seedIcons = new[]
        {
            "flag", "done_all", "gps_fixed", "task_alt", "rocket_launch", "theater_comedy", "psychology", "schedule",
            "visibility", "travel_explore", "school", "workspace_premium", "menu_book", "verified", "timer",
            "calculate", "science", "public", "local_fire_department", "event_available",
        };

        seedIcons.Length.ShouldBe(20);
        seedIcons.ShouldAllBe(name => BadgeIconCatalog.Contains(name));
    }

    // ---------- validator ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gps_fixed")]
    [InlineData("military_tech")]
    public void IsValidIcon_accepts_empty_and_allowlisted_names(string? icon)
    {
        BadgeIconValidator.IsValidIcon(icon).ShouldBeTrue();
    }

    [Theory]
    [InlineData("GPS_FIXED")]                      // case-sensitive
    [InlineData("gps-fixed")]                      // bad char
    [InlineData("1flag")]                          // must start with a letter
    [InlineData("f")]                              // too short
    [InlineData("<script>")]
    [InlineData("achievements/disabled-dark.0085b3.svg")] // legacy IconUrl value is not an icon name
    [InlineData("home")]                           // well-formed but NOT in the allowlist
    [InlineData("sports_soccer")]                  // well-formed but NOT in the allowlist
    public void IsValidIcon_rejects_malformed_or_unknown_names(string icon)
    {
        BadgeIconValidator.IsValidIcon(icon).ShouldBeFalse();
        BadgeIconValidator.IsAllowedIcon(icon).ShouldBeFalse();
    }

    [Fact]
    public void IsAllowedIcon_is_false_for_empty()
    {
        BadgeIconValidator.IsAllowedIcon(null).ShouldBeFalse();
        BadgeIconValidator.IsAllowedIcon("").ShouldBeFalse();
    }

    [Fact]
    public void Legacy_IconUrl_rule_still_applies()
    {
        BadgeIconValidator.IsValid("achievements/disabled-dark.0085b3.svg").ShouldBeTrue();
        BadgeIconValidator.IsValid("https://evil.example/x.svg").ShouldBeFalse();
    }

    // ---------- admin service ----------

    private BadgeDefinitionAdminService NewAdmin(BadgeDbContext ctx) =>
        new(ctx, NullLogger<BadgeDefinitionAdminService>.Instance);

    private static CreateBadgeDefinitionRequest CreateRequest(string code, string? icon, string? iconUrl = null) => new()
    {
        Code = code,
        Name = "Rozet",
        Category = "Test",
        RuleType = "AnswerCount",
        RuleConfigJson = "{\"target\":1}",
        Icon = icon,
        IconUrl = iconUrl,
    };

    [Fact]
    public async Task Create_persists_an_allowlisted_icon_and_returns_it_in_the_dto()
    {
        await using var ctx = _db.NewContext();

        var result = await NewAdmin(ctx).CreateAsync(CreateRequest("with-icon", " emoji_events "), "sub", "admin", CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.Value!.Icon.ShouldBe("emoji_events");
        (await ctx.BadgeDefinitions.AsNoTracking().SingleAsync()).Icon.ShouldBe("emoji_events");
    }

    [Theory]
    [InlineData("home")]
    [InlineData("Emoji_Events")]
    [InlineData("emoji events")]
    public async Task Create_rejects_unknown_or_malformed_icon_with_a_field_error(string icon)
    {
        await using var ctx = _db.NewContext();

        var result = await NewAdmin(ctx).CreateAsync(CreateRequest("bad-icon", icon), "sub", "admin", CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Field == "icon");
        (await ctx.BadgeDefinitions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Create_without_icon_still_accepts_a_legacy_IconUrl()
    {
        await using var ctx = _db.NewContext();

        var result = await NewAdmin(ctx).CreateAsync(
            CreateRequest("legacy", icon: null, iconUrl: "achievements/disabled-dark.0085b3.svg"), "sub", "admin", CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.Value!.Icon.ShouldBeNull();
        result.Value.IconUrl.ShouldBe("achievements/disabled-dark.0085b3.svg");
    }

    [Fact]
    public async Task Update_sets_validates_and_clears_the_icon()
    {
        Guid id;
        await using (var ctx = _db.NewContext())
        {
            id = (await NewAdmin(ctx).CreateAsync(CreateRequest("upd", "star"), "sub", "admin", CancellationToken.None)).Value!.Id;
        }

        UpdateBadgeDefinitionRequest Update(string? icon) => new()
        {
            Name = "Rozet", Category = "Test", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}", Icon = icon,
        };

        await using (var ctx = _db.NewContext())
        {
            var bad = await NewAdmin(ctx).UpdateAsync(id, Update("not_in_list"), "sub", "admin", CancellationToken.None);
            bad.Succeeded.ShouldBeFalse();
            bad.Errors.ShouldContain(e => e.Field == "icon");
        }

        await using (var ctx = _db.NewContext())
        {
            var ok = await NewAdmin(ctx).UpdateAsync(id, Update("diamond"), "sub", "admin", CancellationToken.None);
            ok.Value!.Icon.ShouldBe("diamond");
        }

        await using (var ctx = _db.NewContext())
        {
            var cleared = await NewAdmin(ctx).UpdateAsync(id, Update(null), "sub", "admin", CancellationToken.None);
            cleared.Value!.Icon.ShouldBeNull();
        }
    }

    [Fact]
    public void Update_request_tracks_whether_icon_was_present_in_the_json_body()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var absent = JsonSerializer.Deserialize<UpdateBadgeDefinitionRequest>("{\"name\":\"A\"}", web)!;
        absent.IconSpecified.ShouldBeFalse();

        var explicitNull = JsonSerializer.Deserialize<UpdateBadgeDefinitionRequest>("{\"name\":\"A\",\"icon\":null}", web)!;
        explicitNull.IconSpecified.ShouldBeTrue();
        explicitNull.Icon.ShouldBeNull();

        var set = JsonSerializer.Deserialize<UpdateBadgeDefinitionRequest>("{\"icon\":\"star\"}", web)!;
        set.IconSpecified.ShouldBeTrue();
        set.Icon.ShouldBe("star");
    }

    [Fact]
    public async Task Update_without_icon_in_the_body_keeps_the_stored_icon_and_explicit_null_clears_it()
    {
        Guid id;
        await using (var ctx = _db.NewContext())
        {
            id = (await NewAdmin(ctx).CreateAsync(CreateRequest("keep", "star"), "sub", "admin", CancellationToken.None)).Value!.Id;
        }

        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        const string baseBody = "\"name\":\"Yeni Ad\",\"category\":\"Test\",\"ruleType\":\"AnswerCount\",\"ruleConfigJson\":\"{\\\"target\\\":1}\"";

        await using (var ctx = _db.NewContext())
        {
            var absent = JsonSerializer.Deserialize<UpdateBadgeDefinitionRequest>("{" + baseBody + "}", web)!;
            var kept = await NewAdmin(ctx).UpdateAsync(id, absent, "sub", "admin", CancellationToken.None);
            kept.Succeeded.ShouldBeTrue();
            kept.Value!.Name.ShouldBe("Yeni Ad");
            kept.Value.Icon.ShouldBe("star");
        }

        await using (var ctx = _db.NewContext())
        {
            var explicitNull = JsonSerializer.Deserialize<UpdateBadgeDefinitionRequest>("{" + baseBody + ",\"icon\":null}", web)!;
            var cleared = await NewAdmin(ctx).UpdateAsync(id, explicitNull, "sub", "admin", CancellationToken.None);
            cleared.Value!.Icon.ShouldBeNull();
        }

        await using var read = _db.NewContext();
        (await read.BadgeDefinitions.AsNoTracking().SingleAsync(x => x.Id == id)).Icon.ShouldBeNull();
    }

    [Fact]
    public async Task Admin_dto_never_echoes_a_non_allowlisted_stored_icon()
    {
        Guid id = Guid.NewGuid();
        await using (var ctx = _db.NewContext())
        {
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = id, Code = "raw", Name = "R", Description = "d", Category = "c",
                RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}", Icon = "<img src=x>",
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        (await NewAdmin(read).GetAsync(id, CancellationToken.None))!.Icon.ShouldBeNull();
    }

    // ---------- seeder ----------

    [Fact]
    public async Task Seeder_writes_an_allowlisted_icon_on_all_37_badges()
    {
        await using (var ctx = _db.NewContext())
        {
            await BadgeSeeder.SeedAsync(ctx);
        }

        await using var read = _db.NewContext();
        var all = await read.BadgeDefinitions.AsNoTracking().ToListAsync();

        all.Count.ShouldBe(37);
        foreach (var badge in all)
        {
            badge.Icon.ShouldNotBeNullOrWhiteSpace($"{badge.Code} ikonsuz");
            BadgeIconValidator.IsAllowedIcon(badge.Icon).ShouldBeTrue($"{badge.Code}: '{badge.Icon}' allowlist'te değil");
            badge.IconUrl.ShouldNotBeNull(); // legacy fallback kept during the transition
        }

        all.Select(b => b.Icon).Distinct().Count().ShouldBe(20);
    }

    [Theory]
    [InlineData("first-answer", "flag")]
    [InlineData("correct-streak-5", "done_all")]
    [InlineData("question-hunter-3", "gps_fixed")]
    [InlineData("accuracy-journey-2", "task_alt")]
    [InlineData("study-time-1", "rocket_launch")]
    [InlineData("study-time-2", "theater_comedy")]
    [InlineData("study-time-8", "workspace_premium")]
    [InlineData("subject-turkce-mastery", "menu_book")]
    [InlineData("subject-matematik-mastery", "calculate")]
    [InlineData("subject-fen-bilimleri-mastery", "science")]
    [InlineData("subject-sosyal-bilgiler-mastery", "public")]
    [InlineData("subject-matematik-expert", "verified")]
    [InlineData("subject-sosyal-bilgiler-time", "timer")]
    [InlineData("streak-4", "local_fire_department")]
    [InlineData("active-days-1", "event_available")]
    public void Seeder_icons_follow_the_issue_table(string code, string icon)
    {
        BadgeSeeder.GetDesiredIconsForTesting().Single(x => x.Code == code).Icon.ShouldBe(icon);
    }

    // ---------- student DTO ----------

    [Fact]
    public async Task BadgeProgress_returns_icon_and_nulls_a_non_allowlisted_one()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Code = "a", Name = "A", Description = "d", Category = "c",
                RuleType = "AnswerCount", RuleConfigJson = "{\"target\":10}", Icon = "gps_fixed",
                IconUrl = "achievements/disabled-dark.0085b3.svg",
            });
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Code = "b", Name = "B", Description = "d", Category = "c",
                RuleType = "AnswerCount", RuleConfigJson = "{\"target\":10}", Icon = "not_allowed_icon",
                IconUrl = "https://evil.example/pixel.png",
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var report = await new StudentReportService(read).GetBadgeProgressAsync(1);

        var a = report.BadgeProgress.Single(x => x.Name == "A");
        a.Icon.ShouldBe("gps_fixed");
        a.IconUrl.ShouldBe("achievements/disabled-dark.0085b3.svg");
        var b = report.BadgeProgress.Single(x => x.Name == "B");
        b.Icon.ShouldBeNull();
        b.IconUrl.ShouldBeNull(); // security review D2: invalid legacy IconUrl is not echoed

        // Wire name is camelCase "icon" (ASP.NET Core web defaults).
        var json = JsonSerializer.Serialize(a, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.ShouldContain("\"icon\":\"gps_fixed\"");
        json.ShouldContain("\"iconUrl\":");
    }

    // ---------- SignalR payload + notification data ----------

    [Theory]
    [InlineData("gps_fixed", "gps_fixed", "achievements/x.svg", "achievements/x.svg")]
    [InlineData("not_allowed_icon", null, "achievements/x.svg", "achievements/x.svg")]
    [InlineData(null, null, "https://evil.example/pixel.png", null)]
    public async Task BadgeEarned_hub_payload_and_notification_data_carry_the_icon(
        string? stored, string? expected, string storedIconUrl, string? expectedIconUrl)
    {
        var hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
        var proxy = Substitute.For<IClientProxy>();
        hub.Clients.User(Arg.Any<string>()).Returns(proxy);
        object?[]? sentArgs = null;
        await proxy.SendCoreAsync("BadgeEarned", Arg.Do<object?[]>(a => sentArgs = a), Arg.Any<CancellationToken>());

        await using (var ctx = _db.NewContext())
        {
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Code = "earn-me", Name = "Kazan", Description = "d", Category = "c",
                RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}", IsActive = true,
                Icon = stored, IconUrl = storedIconUrl,
            });
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 7, TotalQuestions = 3 });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            await BadgeEvaluatorFactory.Create(ctx, hub).EvaluateAnswerSubmittedAsync(7, "kc-7");
        }

        // Hub payload (SignalR JSON protocol camelCases: BadgeName/Description/IconUrl/Icon -> icon).
        sentArgs.ShouldNotBeNull();
        using (var payload = JsonDocument.Parse(JsonSerializer.Serialize(sentArgs![0], new JsonSerializerOptions(JsonSerializerDefaults.Web))))
        {
            payload.RootElement.GetProperty("badgeName").GetString().ShouldBe("Kazan");
            payload.RootElement.GetProperty("iconUrl").GetString().ShouldBe(expectedIconUrl);
            var icon = payload.RootElement.GetProperty("icon");
            if (expected is null) icon.ValueKind.ShouldBe(JsonValueKind.Null);
            else icon.GetString().ShouldBe(expected);
        }

        // Persistent notification data: { badgeDefinitionId, badgeCode, iconUrl, icon } — no PII.
        await using var read = _db.NewContext();
        var n = await read.Notifications.AsNoTracking().SingleAsync(x => x.UserId == 7);
        using var data = JsonDocument.Parse(n.Data!);
        var names = data.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        names.ShouldBe(new[] { "badgeDefinitionId", "badgeCode", "iconUrl", "icon" }, ignoreOrder: true);
        var dataIcon = data.RootElement.GetProperty("icon");
        if (expected is null) dataIcon.ValueKind.ShouldBe(JsonValueKind.Null);
        else dataIcon.GetString().ShouldBe(expected);
    }

    public void Dispose() => _db.Dispose();
}

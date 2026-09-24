using System.Threading.RateLimiting;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.StudyLinks;
using ExamApp.Api.Services.StudyLinks;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #61 güvenlik incelemesi: yalnızca onaylı öğretmen + admin yönetebilir; öğretmen yalnızca kendi linkini
/// günceller/siler (admin her linki); denetim kaydı; kapsam başına 30 toplam link; URL normalizasyonu (punycode,
/// AbsoluteUri); IP literal / localhost reddi; yazma rate limit'i.
/// </summary>
public class TopicStudyLinkServiceSecurityTests : IDisposable
{
    private const int OwnerUserId = 600;
    private const int OtherTeacherUserId = 601;
    private const int PendingUserId = 602;
    private const int RejectedUserId = 603;
    private const int AdminUserId = 900;

    private static readonly StudyLinkActor Owner = new(OwnerUserId, "Sahip Öğretmen", "Teacher", false);
    private static readonly StudyLinkActor OtherTeacher = new(OtherTeacherUserId, "Diğer Öğretmen", "Teacher", false);
    private static readonly StudyLinkActor Pending = new(PendingUserId, "Bekleyen", "Teacher", false);
    private static readonly StudyLinkActor Rejected = new(RejectedUserId, "Reddedilen", "Teacher", false);
    private static readonly StudyLinkActor NoProfile = new(4242, "Profilsiz", "Teacher", false);
    private static readonly StudyLinkActor Admin = new(AdminUserId, "Yönetici", "Admin", true);

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static TopicStudyLinkService NewService(AppDbContext ctx) => new(ctx, new ApprovedTeacherGuard(ctx));

    private async Task<(int TopicId, int SubTopicId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        ctx.Teachers.AddRange(
            new Teacher { UserId = OwnerUserId, ApprovalStatus = TeacherApprovalStatus.Approved, Bio = "a" },
            new Teacher { UserId = OtherTeacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, Bio = "b" },
            new Teacher { UserId = PendingUserId, ApprovalStatus = TeacherApprovalStatus.Pending, Bio = "c" },
            new Teacher { UserId = RejectedUserId, ApprovalStatus = TeacherApprovalStatus.Rejected, Bio = "d" });
        await ctx.SaveChangesAsync();

        var topic = new Topic { Name = "Sayılar", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();
        var st = new SubTopic { Name = "Kesirler", TopicId = topic.Id };
        ctx.SubTopics.Add(st);
        await ctx.SaveChangesAsync();
        return (topic.Id, st.Id);
    }

    private async Task<TopicStudyLinkResultDto> CreateAsync(StudyLinkActor actor, int subTopicId,
        string url = "https://example.com/v", bool isActive = true, string title = "Kaynak")
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateAsync(
            new CreateTopicStudyLinkDto { SubTopicId = subTopicId, Title = title, Url = url, IsActive = isActive }, actor);
    }

    private static UpdateTopicStudyLinkDto Upd(string url = "https://example.com/v2", string title = "Yeni", bool? isActive = null) =>
        new() { Title = title, Url = url, IsActive = isActive };

    // ---------------- Onaylı öğretmen ----------------

    public static IEnumerable<object[]> UnapprovedActors() => new[]
    {
        new object[] { Pending }, new object[] { Rejected }, new object[] { NoProfile },
    };

    [Theory]
    [MemberData(nameof(UnapprovedActors))]
    public async Task UnapprovedOrMissingTeacher_IsForbiddenOnEveryManagementOperation(StudyLinkActor actor)
    {
        var (_, st) = await SeedAsync();
        var linkId = (await CreateAsync(Owner, st)).ObjectId;

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var create = await service.CreateAsync(new CreateTopicStudyLinkDto { SubTopicId = st, Title = "x", Url = "https://example.com" }, actor);
        var list = await service.ListAsync(new TopicStudyLinkQueryDto { SubTopicId = st }, actor);
        var get = await service.GetByIdAsync(linkId, actor);
        var update = await service.UpdateAsync(linkId, Upd(), actor);
        var delete = await service.DeleteAsync(linkId, actor);
        var reorder = await service.ReorderAsync(new ReorderTopicStudyLinksDto { SubTopicId = st, Items = { new() { Id = linkId, SortOrder = 5 } } }, actor);

        foreach (var r in new StudyLinkResponseDto[] { create, list, get, update, delete, reorder })
        {
            r.Success.ShouldBeFalse();
            r.Forbidden.ShouldBeTrue();
            r.ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.TeacherNotApproved);
            r.Message.ShouldBe("Çalışma linklerini yönetmek için öğretmen hesabınızın onaylanmış olması gerekir.");
        }

        (await ctx.TopicStudyLinks.AsNoTracking().CountAsync()).ShouldBe(1);
        (await ctx.TopicStudyLinks.AsNoTracking().SingleAsync()).Url.ShouldBe("https://example.com/v");
    }

    [Fact]
    public async Task Admin_WithoutTeacherProfile_CanManage()
    {
        var (_, st) = await SeedAsync();
        var created = await CreateAsync(Admin, st);
        created.Success.ShouldBeTrue();
        created.Link!.CreatedByRole.ShouldBe("Admin");
    }

    [Fact]
    public async Task ApprovedTeacherGuard_ReportsStatus()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var guard = new ApprovedTeacherGuard(ctx);
        (await guard.CheckAsync(OwnerUserId)).ShouldBe(TeacherApprovalCheck.Approved);
        (await guard.CheckAsync(PendingUserId)).ShouldBe(TeacherApprovalCheck.NotApproved);
        (await guard.CheckAsync(RejectedUserId)).ShouldBe(TeacherApprovalCheck.NotApproved);
        (await guard.CheckAsync(4242)).ShouldBe(TeacherApprovalCheck.NoTeacherProfile);
        (await guard.CheckAsync(0)).ShouldBe(TeacherApprovalCheck.NoTeacherProfile);
    }

    // ---------------- Sahiplik ----------------

    [Fact]
    public async Task OtherTeacher_CannotUpdateOrDeleteSomeoneElsesLink()
    {
        var (_, st) = await SeedAsync();
        var linkId = (await CreateAsync(Owner, st)).ObjectId;

        await using (var ctx = _db.NewContext())
        {
            var service = NewService(ctx);
            var update = await service.UpdateAsync(linkId, Upd(url: "https://evil.example/"), OtherTeacher);
            update.Forbidden.ShouldBeTrue();
            update.ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.NotOwner);

            var deactivate = await service.UpdateAsync(linkId, Upd(url: "https://example.com/v", isActive: false), OtherTeacher);
            deactivate.ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.NotOwner);

            var delete = await service.DeleteAsync(linkId, OtherTeacher);
            delete.Forbidden.ShouldBeTrue();
            delete.ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.NotOwner);
        }

        await using var read = _db.NewContext();
        var link = await read.TopicStudyLinks.AsNoTracking().SingleAsync(l => l.Id == linkId);
        link.Url.ShouldBe("https://example.com/v");
        link.IsActive.ShouldBeTrue();
        (await read.TopicStudyLinkAudits.CountAsync(a => a.ActorUserId == OtherTeacherUserId)).ShouldBe(0);
    }

    [Fact]
    public async Task Owner_And_Admin_CanUpdateAndDelete()
    {
        var (_, st) = await SeedAsync();
        var a = (await CreateAsync(Owner, st)).ObjectId;
        var b = (await CreateAsync(Owner, st)).ObjectId;

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        (await service.UpdateAsync(a, Upd(), Owner)).Success.ShouldBeTrue();
        (await service.UpdateAsync(a, Upd(title: "Admin düzeltti"), Admin)).Success.ShouldBeTrue();
        (await service.DeleteAsync(b, Admin)).Success.ShouldBeTrue();
        (await service.DeleteAsync(a, Owner)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task OtherApprovedTeacher_CanReorder_OrderOnlyNoContentChange()
    {
        var (_, st) = await SeedAsync();
        var a = (await CreateAsync(Owner, st, title: "a")).ObjectId;
        var b = (await CreateAsync(Owner, st, title: "b")).ObjectId;

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).ReorderAsync(new ReorderTopicStudyLinksDto
        {
            SubTopicId = st,
            Items = { new() { Id = a, SortOrder = 1 }, new() { Id = b, SortOrder = 0 } }
        }, OtherTeacher);

        result.Success.ShouldBeTrue();
        result.Items.Select(i => i.Id).ShouldBe(new[] { b, a });
        result.Items.ShouldAllBe(i => i.Url == "https://example.com/v");
    }

    // ---------------- Denetim kaydı + updatedBy ----------------

    [Fact]
    public async Task EveryChange_WritesAuditRow_WithActorAndOldNewValues()
    {
        var (_, st) = await SeedAsync();
        var created = await CreateAsync(Owner, st, url: "https://example.com/old", title: "Eski");
        var id = created.ObjectId;
        var other = (await CreateAsync(Owner, st, title: "other")).ObjectId;

        await using (var ctx = _db.NewContext())
        {
            var s = NewService(ctx);
            await s.UpdateAsync(id, Upd(url: "https://example.com/new", title: "Yeni"), Owner);
            await s.UpdateAsync(id, Upd(url: "https://example.com/new", title: "Yeni", isActive: false), Admin);
            await s.UpdateAsync(id, Upd(url: "https://example.com/new", title: "Yeni", isActive: true), Owner);
            await s.ReorderAsync(new ReorderTopicStudyLinksDto
            {
                SubTopicId = st,
                Items = { new() { Id = id, SortOrder = 9 }, new() { Id = other, SortOrder = 1 } } // other zaten 1 → audit yok
            }, OtherTeacher);
            await s.DeleteAsync(id, Admin);
        }

        await using var read = _db.NewContext();
        var audits = await read.TopicStudyLinkAudits.AsNoTracking().Where(a => a.LinkId == id).OrderBy(a => a.Id).ToListAsync();

        audits.Select(a => a.Action).ShouldBe(new[]
        {
            TopicStudyLinkAuditAction.Create, TopicStudyLinkAuditAction.Update, TopicStudyLinkAuditAction.Deactivate,
            TopicStudyLinkAuditAction.Activate, TopicStudyLinkAuditAction.Reorder, TopicStudyLinkAuditAction.Delete,
        });

        audits[0].ShouldSatisfyAllConditions(
            a => a.ActorUserId.ShouldBe(OwnerUserId), a => a.ActorRole.ShouldBe("Teacher"),
            a => a.OldUrl.ShouldBeNull(), a => a.NewUrl.ShouldBe("https://example.com/old"), a => a.NewTitle.ShouldBe("Eski"));
        audits[1].ShouldSatisfyAllConditions(
            a => a.OldUrl.ShouldBe("https://example.com/old"), a => a.NewUrl.ShouldBe("https://example.com/new"),
            a => a.OldTitle.ShouldBe("Eski"), a => a.NewTitle.ShouldBe("Yeni"));
        audits[2].ShouldSatisfyAllConditions(a => a.ActorUserId.ShouldBe(AdminUserId), a => a.ActorRole.ShouldBe("Admin"));
        audits[4].ActorUserId.ShouldBe(OtherTeacherUserId);
        audits[5].ShouldSatisfyAllConditions(
            a => a.OldUrl.ShouldBe("https://example.com/new"), a => a.NewUrl.ShouldBeNull(), a => a.ActorUserId.ShouldBe(AdminUserId));
        audits.ShouldAllBe(a => a.OccurredAtUtc > DateTime.UtcNow.AddMinutes(-5));

        (await read.TopicStudyLinkAudits.CountAsync(a => a.LinkId == other)).ShouldBe(1); // yalnızca Create
    }

    [Fact]
    public async Task RejectedChange_WritesNoAuditRow()
    {
        var (_, st) = await SeedAsync();
        var id = (await CreateAsync(Owner, st)).ObjectId;

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).UpdateAsync(id, Upd(url: "javascript:alert(1)"), Owner)).Success.ShouldBeFalse();

        await using var read = _db.NewContext();
        (await read.TopicStudyLinkAudits.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Dto_ExposesUpdatedBy_NullAfterCreate_SetAfterUpdate()
    {
        var (_, st) = await SeedAsync();
        var created = await CreateAsync(Owner, st);
        created.Link!.UpdatedByUserId.ShouldBeNull();
        created.Link.UpdatedByName.ShouldBeNull();
        created.Link.UpdateTime.ShouldBeNull();

        await using (var ctx = _db.NewContext())
        {
            var updated = await NewService(ctx).UpdateAsync(created.ObjectId, Upd(), Admin);
            updated.Link!.UpdatedByUserId.ShouldBe(AdminUserId);
            updated.Link.UpdatedByName.ShouldBe("Yönetici");
            updated.Link.UpdateTime.ShouldNotBeNull();
        }

        await using var read = _db.NewContext();
        var fromGet = await NewService(read).GetByIdAsync(created.ObjectId, Owner);
        fromGet.Link!.UpdatedByUserId.ShouldBe(AdminUserId);
        fromGet.Link.UpdatedByName.ShouldBe("Yönetici");
    }

    // ---------------- Toplam 30 link ----------------

    [Fact]
    public async Task Create_ThirtyFirstLinkInScope_IsRejected_EvenIfInactive_DeletedNotCounted()
    {
        var (_, st) = await SeedAsync();
        var ids = new List<int>();
        for (var i = 0; i < TopicStudyLinkLimits.MaxTotalLinksPerScope; i++)
            ids.Add((await CreateAsync(Owner, st, isActive: i < 5, title: $"L{i}")).ObjectId);
        ids.ShouldAllBe(id => id > 0);

        var over = await CreateAsync(Owner, st, isActive: false);
        over.Success.ShouldBeFalse();
        over.Conflict.ShouldBeTrue();
        over.ErrorCode.ShouldBe(TopicStudyLinkErrorCodes.TotalLimitReached);
        over.Message.ShouldContain("30");

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).DeleteAsync(ids[^1], Owner)).Success.ShouldBeTrue();

        (await CreateAsync(Owner, st, isActive: false)).Success.ShouldBeTrue();
    }

    // ---------------- URL normalizasyonu ----------------

    [Theory]
    [InlineData("https://Bücher.Example/Kitap?q=1#b", "https://xn--bcher-kva.example/Kitap?q=1#b")]
    [InlineData("HTTPS://WWW.YOUTUBE.COM:443/watch?v=abc", "https://www.youtube.com/watch?v=abc")]
    [InlineData("http://example.com:80/a", "http://example.com/a")]
    [InlineData("https://example.com:8443/a", "https://example.com:8443/a")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/a%20b", "https://example.com/a%20b")]
    public async Task Create_StoresNormalizedAbsoluteUri_WithPunycodeHost(string input, string expected)
    {
        var (_, st) = await SeedAsync();

        var result = await CreateAsync(Owner, st, url: input);

        result.Success.ShouldBeTrue();
        result.Link!.Url.ShouldBe(expected);
        await using var read = _db.NewContext();
        (await read.TopicStudyLinks.AsNoTracking().SingleAsync()).Url.ShouldBe(expected);
    }

    [Fact]
    public async Task Create_YouTubeDetectionUsesNormalizedHost()
    {
        var (_, st) = await SeedAsync();
        (await CreateAsync(Owner, st, url: "https://WWW.YouTube.com/watch?v=1")).Link!.SourceType.ShouldBe(TopicStudyLinkSourceType.YouTube);
        (await CreateAsync(Owner, st, url: "https://youtu.be./x")).Link!.SourceType.ShouldBe(TopicStudyLinkSourceType.YouTube);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.1/admin")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://0x7f.0.0.1/")]
    [InlineData("http://0x7f000001/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://0177.0.0.1/")]
    [InlineData("http://127.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("https://[2001:db8::1]/x")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://localhost/")]
    [InlineData("http://LOCALHOST:8080/x")]
    [InlineData("http://api.localhost/")]
    [InlineData("http://localhost./")]
    public async Task Create_RejectsIpLiteralAndLocalhostHosts(string url)
    {
        var (_, st) = await SeedAsync();

        var result = await CreateAsync(Owner, st, url: url);

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe("Geçerli bir http/https URL giriniz.");
    }

    // ---------------- Rate limit policy ----------------

    [Fact]
    public void WriteRateLimitPolicy_PartitionsPerSub_AndRejectsAfterPermitLimit()
    {
        var options = Substitute.For<IOptionsMonitor<StudyLinkWriteRateLimitOptions>>();
        options.CurrentValue.Returns(new StudyLinkWriteRateLimitOptions { PermitLimit = 2, WindowSeconds = 60 });
        var policy = new StudyLinkWriteRateLimitPolicy(options, NullLogger<StudyLinkWriteRateLimitPolicy>.Instance);

        HttpContext Ctx(string? sub) => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                sub == null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.NameIdentifier, sub) }, "Test"))
        };

        var p1 = policy.GetPartition(Ctx("user-1"));
        var p2 = policy.GetPartition(Ctx("user-2"));
        p1.PartitionKey.ShouldNotBe(p2.PartitionKey);

        using var limiter = p1.Factory(p1.PartitionKey);
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeTrue();
        limiter.AttemptAcquire().IsAcquired.ShouldBeFalse();

        var missing = policy.GetPartition(Ctx(null));
        using var rejectAll = missing.Factory(missing.PartitionKey);
        rejectAll.AttemptAcquire().IsAcquired.ShouldBeFalse();
    }

    [Fact]
    public void WriteRateLimitOptions_DefaultTo30PerMinute()
    {
        var o = new StudyLinkWriteRateLimitOptions();
        o.PermitLimit.ShouldBe(30);
        o.WindowSeconds.ShouldBe(60);
    }
}

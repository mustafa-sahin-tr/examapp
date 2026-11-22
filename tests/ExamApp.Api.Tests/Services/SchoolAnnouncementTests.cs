using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #1281 (#1071 S6): duyuru alıcı çözümü (doğrulanmış öğrenciler, okul genelinde öğretmenler, Active veliler; gönderen hariç,
/// kullanıcı başına tek), yetki (okul geneli / sınıf yalnız okul yöneticisi, başka okul 404) ve outbox parçaları.
/// </summary>
public class SchoolAnnouncementTests : IDisposable
{
    private const int AdminUser = 900;
    private const int TeacherUser = 901;
    private const int SuspendedUser = 902;
    private const int IndependentUser = 903;
    private const int OtherSchoolTeacherUser = 904;
    private const int TwoChildParent = 501;
    private const int PendingParent = 502;
    private const int S8Parent = 503;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public SchoolAnnouncementTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>()
                .Select(id => new UserLookupResultDto { Id = id, FullName = $"Ad{id} Soyad", KeycloakId = $"kc-{id}" })
                .ToList());
    }

    public void Dispose() => _db.Dispose();

    private sealed record Seed(int SchoolA, int SchoolB, int Grade7, int Grade8, int S7a, int S7b, int S8, int Pending, int OtherSchool,
        int Section7A, int EmptySection, int SectionB);

    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "A Okulu" };
        var b = new School { Name = "B Okulu" };
        var g7 = new Grade { Name = "7. Sınıf" };
        var g8 = new Grade { Name = "8. Sınıf" };
        ctx.AddRange(a, b, g7, g8);
        await ctx.SaveChangesAsync();
        var now = DateTime.UtcNow;
        Teacher T(int userId, int? school, bool admin = false, bool suspended = false, bool independent = false) => new()
        {
            UserId = userId, SchoolId = school, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = now.AddDays(-10),
            AccountSuspendedAt = suspended ? now.AddDays(-1) : null, IsIndependentTutor = independent, IsSchoolAdmin = admin
        };
        ctx.Teachers.AddRange(T(AdminUser, a.Id, admin: true), T(TeacherUser, a.Id), T(SuspendedUser, a.Id, suspended: true),
            T(IndependentUser, null, independent: true), T(OtherSchoolTeacherUser, b.Id));
        Student S(int userId, int school, int grade, bool verified = true) => new()
        {
            UserId = userId, StudentNumber = $"2024{userId}", SchoolId = school, GradeId = grade, SchoolVerifiedAt = verified ? now : null
        };
        var s7a = S(101, a.Id, g7.Id);
        var s7b = S(102, a.Id, g7.Id);
        var s8 = S(103, a.Id, g8.Id);
        var pending = S(104, a.Id, g7.Id, verified: false);
        var other = S(105, b.Id, g7.Id);
        ctx.Students.AddRange(s7a, s7b, s8, pending, other);
        var section = new SchoolSection { SchoolId = a.Id, GradeId = g7.Id, Name = "A", NormalizedName = "A" };
        var empty = new SchoolSection { SchoolId = a.Id, GradeId = g8.Id, Name = "B", NormalizedName = "B" };
        var sectionB = new SchoolSection { SchoolId = b.Id, GradeId = g7.Id, Name = "A", NormalizedName = "A" };
        ctx.SchoolSections.AddRange(section, empty, sectionB);
        await ctx.SaveChangesAsync();
        ctx.SchoolSectionMemberships.Add(new SchoolSectionMembership { SectionId = section.Id, StudentId = s7a.Id, StartedAt = now });
        var parents = new[] { new Parent { UserId = TwoChildParent }, new Parent { UserId = PendingParent }, new Parent { UserId = S8Parent } };
        ctx.Parents.AddRange(parents);
        await ctx.SaveChangesAsync();
        ParentStudentLink L(Parent p, Student s, ParentStudentLinkStatus status) => new()
        {
            Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = p.Id, StudentId = s.Id, Status = status, CreatedAt = now,
            ActivatedAt = status == ParentStudentLinkStatus.Active ? now : null
        };
        ctx.ParentStudentLinks.AddRange(L(parents[0], s7a, ParentStudentLinkStatus.Active), L(parents[0], s7b, ParentStudentLinkStatus.Active),
            L(parents[1], s8, ParentStudentLinkStatus.Pending), L(parents[2], s8, ParentStudentLinkStatus.Active));
        await ctx.SaveChangesAsync();
        return new Seed(a.Id, b.Id, g7.Id, g8.Id, s7a.Id, s7b.Id, s8.Id, pending.Id, other.Id, section.Id, empty.Id, sectionB.Id);
    }

    private SchoolAnnouncementService Service(AppDbContext ctx) =>
        new(ctx, new SchoolSectionService(ctx, _authApi), new SchoolAdminResolver(ctx), _authApi);

    private static SchoolAnnouncementSendRequestDto Req(string target, int? gradeId = null, int? sectionId = null,
        List<int>? studentIds = null, string title = "Veli toplantısı", string body = "Cuma 15.00'te.") =>
        new() { Target = target, GradeId = gradeId, SectionId = sectionId, StudentIds = studentIds, Title = title, Body = body };

    private async Task<SchoolAnnouncementResult> SendAsync(int sender, SchoolAnnouncementSendRequestDto req)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx).SendAsync(sender, req);
    }

    private async Task<List<AnnouncementPublishedEvent>> EventsAsync()
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<AnnouncementPublishedEvent>();
        // OutboxMessage.Id Guid: sıra duyuru + parça numarasından.
        return (await ctx.OutboxMessages.AsNoTracking().Where(m => m.Type == type).ToListAsync())
            .Select(m => JsonSerializer.Deserialize<AnnouncementPublishedEvent>(m.Content)!)
            .OrderBy(e => e.AnnouncementId)
            .ThenBy(e => e.ChunkIndex)
            .ToList();
    }

    [Fact]
    public async Task School_wide_reaches_verified_students_school_teachers_and_active_parents_once()
    {
        await SeedAsync();

        var result = await SendAsync(AdminUser, Req("School"));

        result.Status.ShouldBe(SchoolAnnouncementStatus.Success);
        var dto = result.Announcement!;
        (dto.StudentCount, dto.TeacherCount, dto.ParentCount).ShouldBe((3, 1, 2));
        var e = (await EventsAsync()).ShouldHaveSingleItem();
        e.AnnouncementId.ShouldBe(dto.Id);
        e.SchoolName.ShouldBe("A Okulu");
        e.Title.ShouldBe("Veli toplantısı");
        e.SenderDisplayName.ShouldNotBeNullOrWhiteSpace();
        (e.ChunkIndex, e.ChunkCount).ShouldBe((0, 1));
        e.Recipients.Select(r => (r.Audience, r.UserId)).ShouldBe(
        [
            ("Student", 101), ("Student", 102), ("Student", 103), ("Teacher", TeacherUser), ("Parent", TwoChildParent), ("Parent", S8Parent)
        ], ignoreOrder: true);
        e.Recipients.Select(r => r.NotificationId).Distinct().Count().ShouldBe(6);
        e.Recipients.ShouldAllBe(r => r.KeycloakId == $"kc-{r.UserId}");
        // İki çocuğu hedeflenen veliye ad yazılmaz; tek çocuklu veliye çocuğun kısa adı.
        e.Recipients.Single(r => r.UserId == TwoChildParent).StudentDisplayName.ShouldBeEmpty();
        e.Recipients.Single(r => r.UserId == S8Parent).StudentDisplayName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Grade_target_is_admin_only_and_skips_teachers()
    {
        var s = await SeedAsync();

        (await SendAsync(TeacherUser, Req("Grade", gradeId: s.Grade7))).Status.ShouldBe(SchoolAnnouncementStatus.Forbidden);
        (await SendAsync(TeacherUser, Req("School"))).Status.ShouldBe(SchoolAnnouncementStatus.Forbidden);
        var result = await SendAsync(AdminUser, Req("Grade", gradeId: s.Grade7));

        result.Status.ShouldBe(SchoolAnnouncementStatus.Success);
        result.Announcement!.TargetLabel.ShouldBe("7. Sınıf");
        (await EventsAsync()).ShouldHaveSingleItem().Recipients.Select(r => r.UserId)
            .ShouldBe([101, 102, TwoChildParent], ignoreOrder: true); // bekleyen 104 yok
    }

    [Fact]
    public async Task Teacher_can_send_to_a_section_or_chosen_students_of_own_school()
    {
        var s = await SeedAsync();

        var section = await SendAsync(TeacherUser, Req("Section", sectionId: s.Section7A));
        section.Status.ShouldBe(SchoolAnnouncementStatus.Success);
        section.Announcement!.TargetLabel.ShouldBe("7. Sınıf / A");
        var chosen = await SendAsync(TeacherUser, Req("Students", studentIds: [s.S8, s.S8]));
        chosen.Status.ShouldBe(SchoolAnnouncementStatus.Success);

        var events = await EventsAsync();
        events[0].Recipients.Select(r => r.UserId).ShouldBe([101, TwoChildParent], ignoreOrder: true);
        events[0].Recipients.Single(r => r.UserId == TwoChildParent).StudentDisplayName.ShouldNotBeNullOrWhiteSpace();
        events[1].Recipients.Select(r => r.UserId).ShouldBe([103, S8Parent], ignoreOrder: true); // Pending veli yok
    }

    [Fact]
    public async Task Other_school_or_unverified_targets_are_not_found_and_nothing_is_written()
    {
        var s = await SeedAsync();

        (await SendAsync(TeacherUser, Req("Section", sectionId: s.SectionB))).Status.ShouldBe(SchoolAnnouncementStatus.NotFound);
        (await SendAsync(TeacherUser, Req("Students", studentIds: [s.S7a, s.OtherSchool]))).Status.ShouldBe(SchoolAnnouncementStatus.NotFound);
        (await SendAsync(TeacherUser, Req("Students", studentIds: [s.Pending]))).Status.ShouldBe(SchoolAnnouncementStatus.NotFound);
        (await SendAsync(OtherSchoolTeacherUser, Req("Section", sectionId: s.Section7A))).Status.ShouldBe(SchoolAnnouncementStatus.NotFound);
        (await SendAsync(SuspendedUser, Req("Section", sectionId: s.Section7A))).Status.ShouldBe(SchoolAnnouncementStatus.Forbidden);
        (await SendAsync(IndependentUser, Req("Students", studentIds: [s.S7a]))).Status.ShouldBe(SchoolAnnouncementStatus.Forbidden);
        (await SendAsync(TeacherUser, Req("Section", sectionId: s.EmptySection))).Status.ShouldBe(SchoolAnnouncementStatus.NoRecipients);

        await using var ctx = _db.NewContext();
        (await ctx.SchoolAnnouncements.CountAsync()).ShouldBe(0);
        (await ctx.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("Section", "", "x")]
    [InlineData("Section", "x", "")]
    [InlineData("Everyone", "x", "x")]
    [InlineData("section", "x", "x")]
    public async Task Invalid_requests_are_rejected(string target, string title, string body)
    {
        var s = await SeedAsync();
        (await SendAsync(TeacherUser, Req(target, sectionId: s.Section7A, title: title, body: body)))
            .Status.ShouldBe(SchoolAnnouncementStatus.Invalid);
    }

    [Fact]
    public async Task Length_and_list_limits_are_enforced()
    {
        var s = await SeedAsync();
        (await SendAsync(TeacherUser, Req("Section", sectionId: s.Section7A, title: new string('t', 101)))).Status
            .ShouldBe(SchoolAnnouncementStatus.Invalid);
        (await SendAsync(TeacherUser, Req("Section", sectionId: s.Section7A, body: new string('b', 501)))).Status
            .ShouldBe(SchoolAnnouncementStatus.Invalid);
        (await SendAsync(AdminUser, Req("Grade"))).Status.ShouldBe(SchoolAnnouncementStatus.Invalid);
        (await SendAsync(TeacherUser, Req("Students", studentIds: Enumerable.Range(1, 201).ToList()))).Status
            .ShouldBe(SchoolAnnouncementStatus.Invalid);
    }

    [Fact]
    public async Task Daily_limit_returns_rate_limited()
    {
        var s = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.SchoolAnnouncements.AddRange(Enumerable.Range(0, SchoolAnnouncementService.DailyLimit).Select(i => new SchoolAnnouncement
            {
                SchoolId = s.SchoolA, SenderUserId = TeacherUser, Target = SchoolAnnouncementTarget.Section, Title = "t", Body = "b",
                CreatedAt = DateTime.UtcNow.AddHours(-i)
            }));
            await ctx.SaveChangesAsync();
        }

        (await SendAsync(TeacherUser, Req("Section", sectionId: s.Section7A))).Status.ShouldBe(SchoolAnnouncementStatus.RateLimited);
        (await SendAsync(AdminUser, Req("Section", sectionId: s.Section7A))).Status.ShouldBe(SchoolAnnouncementStatus.Success);
    }

    [Fact]
    public async Task Large_audience_is_split_into_chunks()
    {
        var s = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Students.AddRange(Enumerable.Range(0, AnnouncementPublishedEvent.MaxRecipientsPerEvent).Select(i => new Student
            {
                UserId = 10_000 + i, StudentNumber = $"9{i}", SchoolId = s.SchoolA, GradeId = s.Grade8, SchoolVerifiedAt = DateTime.UtcNow
            }));
            await ctx.SaveChangesAsync();
        }

        var result = await SendAsync(AdminUser, Req("Grade", gradeId: s.Grade8));

        result.Announcement!.StudentCount.ShouldBe(AnnouncementPublishedEvent.MaxRecipientsPerEvent + 1);
        var events = await EventsAsync();
        events.Select(e => e.Recipients.Count).ShouldBe([500, 2]); // 501 öğrenci + 1 veli
        events.ShouldAllBe(e => e.ChunkCount == 2 && e.AnnouncementId == result.Announcement.Id);
        events.Select(e => e.EventId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Sent_list_shows_own_announcements_newest_first_even_after_school_change()
    {
        var s = await SeedAsync();
        await SendAsync(TeacherUser, Req("Section", sectionId: s.Section7A, title: "Birinci"));
        await SendAsync(TeacherUser, Req("Students", studentIds: [s.S8], title: "İkinci"));
        await SendAsync(AdminUser, Req("School", title: "Yönetici"));
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.UserId == TeacherUser).ExecuteUpdateAsync(u => u.SetProperty(t => t.SchoolId, s.SchoolB));

        await using var check = _db.NewContext();
        var page = await Service(check).ListSentAsync(TeacherUser, 1, 20);
        page.Items.Select(i => i.Title).ShouldBe(["İkinci", "Birinci"]);
        page.Items[0].Target.ShouldBe("Students");
        page.Items[0].StudentCount.ShouldBe(1);
        page.Items[0].ParentCount.ShouldBe(1);
        // Yeni duyuru güncel okula gider: A okulunun şubesi artık 404.
        (await Service(check).SendAsync(TeacherUser, Req("Section", sectionId: s.Section7A))).Status
            .ShouldBe(SchoolAnnouncementStatus.NotFound);
    }
}

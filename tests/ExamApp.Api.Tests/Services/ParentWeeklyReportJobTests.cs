using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1112: son biten haftanın raporu planında <c>weekly-report</c> olan çocuğun her Active velisine bağlantı başına hafta
/// başına bir kez gider; rapor çocuk başına bir kez hesaplanır, e-postayı kapatan veliye adres boş yazılır, hakkı olmayan ve koparılmış
/// bağlantı atlanır. Sayıların kendisi <c>ParentDashboardServiceTests</c>'te (panelle aynı hesap). SQLite.
/// </summary>
public class ParentWeeklyReportJobTests : IDisposable
{
    private const int StudentUser = 8601;
    private const int OtherStudentUser = 8602;
    private const int ParentUser = 8611;
    private const int SecondParentUser = 8612;
    private static readonly DateOnly Week = new(2027, 3, 1);

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly IParentWeeklyReportService _reports = Substitute.For<IParentWeeklyReportService>();
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();
    private readonly HashSet<int> _entitledUsers = [StudentUser];

    public ParentWeeklyReportJobTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto
            {
                Id = id, KeycloakId = $"kc-{id}", FullName = id == StudentUser ? "Ayşe Kaya" : $"Kişi {id}", Email = $"u{id}@example.com"
            }).ToList());
        _reports.LastCompletedWeek.Returns(Week);
        _reports.BuildAsync(Arg.Any<ParentChildAccessGrant>(), Week, Arg.Any<CancellationToken>())
            .Returns(ci => new ParentWeeklyReportDto
            {
                StudentId = ci.Arg<ParentChildAccessGrant>().StudentId,
                WeekStart = Week,
                QuestionsSolved = 40,
                Correct = 30,
                ActiveDays = 4,
                StudyMinutes = 95,
                Points = 120,
                Subjects = [new ParentWeeklySubjectDto { SubjectId = 1, SubjectName = "Matematik", Answered = 40, Correct = 30, AccuracyPercent = 75 }],
                DroppedTopics = [new ParentWeeklyTopicDropDto { TopicId = 2, TopicName = "Kesirler", AccuracyPercent = 40, PreviousAccuracyPercent = 70 }],
                MockExams = [new ParentWeeklyMockExamDto { Name = "Deneme 1", Net = 31.25m, Rank = 3, Takers = 20 }],
                Badges = [new ParentWeeklyBadgeDto { Name = "İlk adım" }],
                Suggestion = "reviewTopic",
                SuggestionTopic = "Kesirler",
            });
        _entitlements.CheckAsync(Arg.Any<EntitlementSubject>(), EntitlementFeatures.WeeklyReport, Arg.Any<CancellationToken>())
            .Returns(ci => _entitledUsers.Contains(ci.Arg<EntitlementSubject>().UserId)
                ? new EntitlementCheck(true, null, 0, null, "student-pro")
                : new EntitlementCheck(false, 0, 0, null, "free"));
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedStudentAsync(int studentUserId, string number)
    {
        await using var ctx = _db.NewContext();
        var student = new Student { UserId = studentUserId, StudentNumber = number };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        return student.Id;
    }

    private async Task<int> LinkAsync(int parentUserId, int studentId, bool emailOptOut = false,
        ParentStudentLinkStatus status = ParentStudentLinkStatus.Active)
    {
        await using var ctx = _db.NewContext();
        var parent = await ctx.Parents.FirstOrDefaultAsync(p => p.UserId == parentUserId);
        if (parent is null)
        {
            parent = new Parent { UserId = parentUserId, WeeklyReportEmailOptOut = emailOptOut };
            ctx.Parents.Add(parent);
            await ctx.SaveChangesAsync();
        }
        var link = new ParentStudentLink
        {
            Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = parent.Id, StudentId = studentId, Status = status,
            CreatedAt = DateTime.UtcNow.AddDays(-30), ActivatedAt = DateTime.UtcNow.AddDays(-30),
            RevokedAt = status == ParentStudentLinkStatus.Revoked ? DateTime.UtcNow.AddDays(-1) : null,
        };
        ctx.ParentStudentLinks.Add(link);
        await ctx.SaveChangesAsync();
        return link.Id;
    }

    private async Task<int> RunAsync()
    {
        await using var ctx = _db.NewContext();
        return await new ParentWeeklyReportJob(ctx, _reports, _entitlements, _authApi,
            new StaticOptionsMonitor<ParentWeeklyReportOptions>(new ParentWeeklyReportOptions { BatchSize = 1 })).RunAsync();
    }

    private async Task<List<ParentWeeklyReportEvent>> EventsAsync()
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<ParentWeeklyReportEvent>();
        return (await ctx.OutboxMessages.Where(o => o.Type == type).ToListAsync())
            .Select(o => JsonSerializer.Deserialize<ParentWeeklyReportEvent>(o.Content)!).ToList();
    }

    [Fact]
    public async Task Each_active_parent_gets_the_week_once_and_the_report_is_built_once_per_child()
    {
        var student = await SeedStudentAsync(StudentUser, "s1");
        var first = await LinkAsync(ParentUser, student);
        await LinkAsync(SecondParentUser, student, emailOptOut: true);

        (await RunAsync()).ShouldBe(2);
        (await RunAsync()).ShouldBe(0);

        var events = await EventsAsync();
        events.Count.ShouldBe(2);
        await _reports.Received(1).BuildAsync(Arg.Any<ParentChildAccessGrant>(), Week, Arg.Any<CancellationToken>());

        var e = events.Single(x => x.RecipientUserId == ParentUser);
        e.StudentId.ShouldBe(student);
        e.StudentDisplayName.ShouldBe("Ayşe K.");
        e.WeekStart.ShouldBe(new DateTime(2027, 3, 1));
        (e.QuestionsSolved, e.Correct, e.ActiveDays, e.StudyMinutes, e.Points).ShouldBe((40, 30, 4, 95, 120));
        e.Subjects.Single().Name.ShouldBe("Matematik");
        e.DroppedTopics.Single().PreviousAccuracyPercent.ShouldBe(70);
        e.MockExams.Single().Rank.ShouldBe(3);
        e.Badges.ShouldBe(["İlk adım"]);
        (e.Suggestion, e.SuggestionTopic).ShouldBe(("reviewTopic", "Kesirler"));
        e.RecipientKeycloakId.ShouldBe($"kc-{ParentUser}");
        e.RecipientEmail.ShouldBe($"u{ParentUser}@example.com");
        events.Single(x => x.RecipientUserId == SecondParentUser).RecipientEmail.ShouldBeEmpty();

        await using var ctx = _db.NewContext();
        (await ctx.ParentStudentLinks.SingleAsync(l => l.Id == first)).WeeklyReportSentFor.ShouldBe(Week);
    }

    [Fact]
    public async Task Child_without_the_feature_is_skipped_until_the_plan_has_it()
    {
        var student = await SeedStudentAsync(OtherStudentUser, "s2");
        var link = await LinkAsync(ParentUser, student);

        (await RunAsync()).ShouldBe(0);
        await using (var ctx = _db.NewContext())
            (await ctx.ParentStudentLinks.SingleAsync(l => l.Id == link)).WeeklyReportSentFor.ShouldBeNull();

        _entitledUsers.Add(OtherStudentUser);
        (await RunAsync()).ShouldBe(1);
        (await EventsAsync()).Single().RecipientUserId.ShouldBe(ParentUser);
    }

    [Fact]
    public async Task Revoked_and_pending_links_get_nothing_and_last_weeks_mark_is_resent()
    {
        var student = await SeedStudentAsync(StudentUser, "s1");
        await LinkAsync(ParentUser, student, status: ParentStudentLinkStatus.Revoked);
        await LinkAsync(SecondParentUser, student, status: ParentStudentLinkStatus.Pending);
        (await RunAsync()).ShouldBe(0);

        var other = await SeedStudentAsync(OtherStudentUser, "s2");
        _entitledUsers.Add(OtherStudentUser);
        var link = await LinkAsync(ParentUser, other);
        await using (var ctx = _db.NewContext())
        {
            await ctx.ParentStudentLinks.Where(l => l.Id == link)
                .ExecuteUpdateAsync(u => u.SetProperty(l => l.WeeklyReportSentFor, Week.AddDays(-7)));
        }
        (await RunAsync()).ShouldBe(1);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

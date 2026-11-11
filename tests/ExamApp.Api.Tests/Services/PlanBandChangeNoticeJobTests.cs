using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1110: satın almadan sonra sınıf bandı değişen (8→9) öğrencinin planı bitmeden 30 gün önce Active velilere bir uyarı; band
/// aynı kaldıysa yalnız işaret; atlama satın almadan önceyse aday değil; geri alınan atlama sayılmaz. SQLite.
/// </summary>
public class PlanBandChangeNoticeJobTests : IDisposable
{
    private const int StudentUser = 8401;
    private const int ParentUserA = 8411;
    private const int ParentUserB = 8412;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    // 2027-08-20 12:00 UTC: 1 Ağustos atlamasından sonra.
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2027, 8, 20, 12, 0, 0, TimeSpan.Zero));

    public PlanBandChangeNoticeJobTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto
            {
                Id = id, KeycloakId = $"kc-{id}", FullName = id == StudentUser ? "Ayşe Kaya" : $"Veli {id}", Email = $"u{id}@example.com"
            }).ToList());
    }

    public void Dispose() => _db.Dispose();

    private DateTime Now => _time.Now.UtcDateTime;

    private sealed record World(int StudentId, int ParentA, int ParentB, Dictionary<int, int> GradeOfLevel);

    /// <summary>Öğrenci <paramref name="from"/>. sınıftan <paramref name="to"/>. sınıfa 1 Ağustos 2027'de atladı; A Active, B Pending.</summary>
    private async Task<World> SeedAsync(int from, int to, bool reverted = false)
    {
        await using var ctx = _db.NewContext();
        var grades = Enumerable.Range(1, 12).Select(l => new Grade { Name = $"{l}. Sınıf", Code = $"{l}-sinif" }).ToList();
        ctx.Grades.AddRange(grades);
        await ctx.SaveChangesAsync();
        var g = grades.Select((x, i) => (Level: i + 1, x.Id)).ToDictionary(x => x.Level, x => x.Id);

        var student = new Student { UserId = StudentUser, StudentNumber = "s1", GradeId = g[to] };
        var a = new Parent { UserId = ParentUserA };
        var b = new Parent { UserId = ParentUserB };
        ctx.AddRange(student, a, b);
        await ctx.SaveChangesAsync();
        foreach (var (parentId, status) in new[] { (a.Id, ParentStudentLinkStatus.Active), (b.Id, ParentStudentLinkStatus.Pending) })
        {
            ctx.ParentStudentLinks.Add(new ParentStudentLink
            {
                Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = parentId, StudentId = student.Id, Status = status,
                CreatedAt = Now.AddDays(-100), ActivatedAt = status == ParentStudentLinkStatus.Active ? Now.AddDays(-100) : null,
            });
        }
        ctx.StudentGradePromotions.Add(new StudentGradePromotion
        {
            StudentId = student.Id, Year = 2027, FromGradeId = g[from], ToGradeId = g[to],
            PromotedAtUtc = new DateTime(2027, 7, 31, 21, 10, 0, DateTimeKind.Utc),
            RevertedAtUtc = reverted ? new DateTime(2027, 8, 2, 0, 0, 0, DateTimeKind.Utc) : null,
        });
        await ctx.SaveChangesAsync();
        return new World(student.Id, a.Id, b.Id, g);
    }

    private async Task<int> AssignAsync(DateTime from, DateTime to, string planKey = SubscriptionPlan.StudentProKey)
    {
        await using var ctx = _db.NewContext();
        var planId = await ctx.SubscriptionPlans.Where(p => p.Key == planKey).Select(p => p.Id).SingleAsync();
        var a = new UserSubscriptionPlan { UserId = StudentUser, PlanId = planId, ValidFromUtc = from, ValidToUtc = to };
        ctx.UserSubscriptionPlans.Add(a);
        await ctx.SaveChangesAsync();
        return a.Id;
    }

    private async Task<int> RunAsync()
    {
        await using var ctx = _db.NewContext();
        return await new PlanBandChangeNoticeJob(ctx, _authApi,
            new StaticOptionsMonitor<PlanBandChangeNoticeOptions>(new PlanBandChangeNoticeOptions()), _time).RunAsync();
    }

    private async Task<List<ParentChildPlanEndingEvent>> EventsAsync()
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<ParentChildPlanEndingEvent>();
        return (await ctx.OutboxMessages.Where(o => o.Type == type).ToListAsync())
            .Select(o => JsonSerializer.Deserialize<ParentChildPlanEndingEvent>(o.Content)!).ToList();
    }

    private async Task<DateTime?> MarkOfAsync(int assignmentId)
    {
        await using var ctx = _db.NewContext();
        return (await ctx.UserSubscriptionPlans.SingleAsync(a => a.Id == assignmentId)).BandChangeNoticeSentAtUtc;
    }

    [Fact]
    public async Task Band_change_notifies_each_active_parent_once_thirty_days_before_the_end()
    {
        var w = await SeedAsync(from: 8, to: 9);
        var assignmentId = await AssignAsync(new DateTime(2027, 6, 1), Now.AddDays(32));

        (await RunAsync()).ShouldBe(0); // pencere dışında
        _time.Now = _time.Now.AddDays(3);
        (await RunAsync()).ShouldBe(1);
        (await RunAsync()).ShouldBe(0);

        var e = (await EventsAsync()).ShouldHaveSingleItem();
        e.ParentUserId.ShouldBe(ParentUserA);
        e.NewGradeLevel.ShouldBe(9);
        e.IsTrial.ShouldBeFalse();
        e.AssignmentId.ShouldBe(assignmentId);
        e.StudentDisplayName.ShouldBe("Ayşe K.");
        (await MarkOfAsync(assignmentId)).ShouldBe(Now);
    }

    [Fact]
    public async Task Same_band_is_marked_without_a_notice()
    {
        await SeedAsync(from: 6, to: 7);
        var assignmentId = await AssignAsync(new DateTime(2027, 6, 1), Now.AddDays(10));

        (await RunAsync()).ShouldBe(1);

        (await EventsAsync()).ShouldBeEmpty();
        (await MarkOfAsync(assignmentId)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Plan_bought_after_the_promotion_is_not_a_candidate()
    {
        await SeedAsync(from: 11, to: 12);
        var assignmentId = await AssignAsync(new DateTime(2027, 8, 5), Now.AddDays(10));

        (await RunAsync()).ShouldBe(0);
        (await MarkOfAsync(assignmentId)).ShouldBeNull();
    }

    [Fact]
    public async Task Reverted_promotion_does_not_count()
    {
        await SeedAsync(from: 4, to: 5, reverted: true);
        await AssignAsync(new DateTime(2027, 6, 1), Now.AddDays(10));

        (await RunAsync()).ShouldBe(0);
        (await EventsAsync()).ShouldBeEmpty();
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

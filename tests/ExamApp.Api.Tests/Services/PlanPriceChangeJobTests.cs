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
/// Issue #1116: planlanmış fiyat artışı süren satın almanın ödeyenine (Active veli; velisi yoksa öğrencinin kendisi; öğretmen) değişiklik
/// başına bir kez bildirilir, vadesi gelince fiyata geçer. Fiyat satırı öğrencinin bandı / kapsamı / süresiyle eşleşmeli; iptal edilen,
/// başka seçenekteki ya da yaz satın alması bildirilmez. Admin kuralı: artış +30 gün, aynı artış korunur, indirim hemen. SQLite.
/// </summary>
public class PlanPriceChangeJobTests : IDisposable
{
    private const int StudentUser = 8501;
    private const int ParentUser = 8511;
    private const int TeacherUser = 8521;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2027, 3, 10, 12, 0, 0, TimeSpan.Zero));

    public PlanPriceChangeJobTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto
            {
                Id = id, KeycloakId = $"kc-{id}", FullName = id == StudentUser ? "Ayşe Kaya" : $"Kişi {id}", Email = $"u{id}@example.com"
            }).ToList());
    }

    public void Dispose() => _db.Dispose();

    private DateTime Now => _time.Now.UtcDateTime;

    /// <summary>7. sınıf öğrencisi (Middle band); <paramref name="withParent"/> ise Active velisi var.</summary>
    private async Task<int> SeedStudentAsync(bool withParent = true)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7. Sınıf", Code = "7-sinif" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var student = new Student { UserId = StudentUser, StudentNumber = "s1", GradeId = grade.Id };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        if (withParent)
        {
            var parent = new Parent { UserId = ParentUser };
            ctx.Parents.Add(parent);
            await ctx.SaveChangesAsync();
            ctx.ParentStudentLinks.Add(new ParentStudentLink
            {
                Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = parent.Id, StudentId = student.Id,
                Status = ParentStudentLinkStatus.Active, CreatedAt = Now.AddDays(-50), ActivatedAt = Now.AddDays(-50),
            });
            await ctx.SaveChangesAsync();
        }
        return student.Id;
    }

    private async Task<int> BuyAsync(int userId, string planKey, PlanDuration duration, int? scopeSubjectId = null,
        DateTime? cancelledAt = null)
    {
        await using var ctx = _db.NewContext();
        var planId = await ctx.SubscriptionPlans.Where(p => p.Key == planKey).Select(p => p.Id).SingleAsync();
        var a = new UserSubscriptionPlan
        {
            UserId = userId, PlanId = planId, ValidFromUtc = Now.AddDays(-5), ValidToUtc = StudentPlanPricing.ValidTo(Now.AddDays(-5), duration),
            Duration = duration, PricePaid = 100, ScopeSubjectId = scopeSubjectId, CancelledAtUtc = cancelledAt,
        };
        ctx.UserSubscriptionPlans.Add(a);
        await ctx.SaveChangesAsync();
        return a.Id;
    }

    /// <summary>Planın Middle / bütün dersler / <paramref name="duration"/> satırına bekleyen artış yazar; satırın bugünkü fiyatını döner.</summary>
    private async Task<decimal> ScheduleRowAsync(string planKey, PlanDuration duration, decimal next, DateTime from)
    {
        await using var ctx = _db.NewContext();
        var row = await ctx.SubscriptionPlanPrices.SingleAsync(p => p.Plan.Key == planKey && p.Band == StudentGradeBand.Middle
            && p.GradeLevel == null && p.Scope == PlanScope.AllSubjects && p.Duration == duration);
        (row.PendingPrice, row.PendingFromUtc) = (next, from);
        await ctx.SaveChangesAsync();
        return row.Price;
    }

    private async Task<int> RunAsync()
    {
        await using var ctx = _db.NewContext();
        return await new PlanPriceChangeJob(ctx, _authApi,
            new StaticOptionsMonitor<PlanPriceChangeOptions>(new PlanPriceChangeOptions { BatchSize = 1 }), _time).RunAsync();
    }

    private async Task<List<PlanPriceChangeNoticeEvent>> EventsAsync()
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<PlanPriceChangeNoticeEvent>();
        return (await ctx.OutboxMessages.Where(o => o.Type == type).ToListAsync())
            .Select(o => JsonSerializer.Deserialize<PlanPriceChangeNoticeEvent>(o.Content)!).ToList();
    }

    [Fact]
    public async Task Increase_is_announced_to_the_active_parent_once_then_applied_on_its_date()
    {
        var studentId = await SeedStudentAsync();
        var assignmentId = await BuyAsync(StudentUser, SubscriptionPlan.StudentProKey, PlanDuration.TwelveMonths);
        var from = Now.AddDays(30);
        var current = await ScheduleRowAsync(SubscriptionPlan.StudentProKey, PlanDuration.TwelveMonths, 9999, from);

        (await RunAsync()).ShouldBe(1);
        (await RunAsync()).ShouldBe(0);

        var e = (await EventsAsync()).ShouldHaveSingleItem();
        e.RecipientUserId.ShouldBe(ParentUser);
        e.RecipientEmail.ShouldBe($"u{ParentUser}@example.com");
        e.StudentId.ShouldBe(studentId);
        e.StudentDisplayName.ShouldBe("Ayşe K.");
        e.AssignmentId.ShouldBe(assignmentId);
        (e.CurrentPrice, e.NewPrice, e.Duration).ShouldBe((current, 9999m, nameof(PlanDuration.TwelveMonths)));
        e.EffectiveFromUtc.ShouldBe(from);

        _time.Now = _time.Now.AddDays(31);
        await RunAsync();
        await using var ctx = _db.NewContext();
        var row = await ctx.SubscriptionPlanPrices.SingleAsync(p => p.Plan.Key == SubscriptionPlan.StudentProKey
            && p.Band == StudentGradeBand.Middle && p.GradeLevel == null && p.Scope == PlanScope.AllSubjects && p.Duration == PlanDuration.TwelveMonths);
        (row.Price, row.PendingPrice, row.PendingFromUtc).ShouldBe((9999m, (decimal?)null, (DateTime?)null));
    }

    [Fact]
    public async Task Student_without_a_parent_is_told_themself_and_a_rescheduled_change_is_announced_again()
    {
        await SeedStudentAsync(withParent: false);
        await BuyAsync(StudentUser, SubscriptionPlan.StudentProKey, PlanDuration.Monthly);
        await ScheduleRowAsync(SubscriptionPlan.StudentProKey, PlanDuration.Monthly, 999, Now.AddDays(30));
        (await RunAsync()).ShouldBe(1);

        await ScheduleRowAsync(SubscriptionPlan.StudentProKey, PlanDuration.Monthly, 1099, Now.AddDays(31));
        (await RunAsync()).ShouldBe(1);

        var events = await EventsAsync();
        events.Select(x => (x.RecipientUserId, x.StudentId, x.NewPrice)).ShouldBe([(StudentUser, (int?)null, 999m), (StudentUser, null, 1099m)]);
    }

    [Fact]
    public async Task Other_option_cancelled_and_summer_purchases_are_not_announced()
    {
        await SeedStudentAsync();
        await ScheduleRowAsync(SubscriptionPlan.StudentProKey, PlanDuration.TwelveMonths, 9999, Now.AddDays(30));

        await BuyAsync(StudentUser, SubscriptionPlan.StudentProKey, PlanDuration.SixMonths);
        (await RunAsync()).ShouldBe(0);

        await using (var ctx = _db.NewContext())
            await ctx.UserSubscriptionPlans.ExecuteDeleteAsync();
        await BuyAsync(StudentUser, SubscriptionPlan.StudentProKey, PlanDuration.TwelveMonths, cancelledAt: Now.AddDays(-1));
        (await RunAsync()).ShouldBe(0);

        (await EventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Teacher_monthly_increase_is_announced_to_the_teacher()
    {
        var assignmentId = await BuyAsync(TeacherUser, SubscriptionPlan.TeacherProKey, PlanDuration.Monthly);
        await using (var ctx = _db.NewContext())
        {
            var plan = await ctx.SubscriptionPlans.SingleAsync(p => p.Key == SubscriptionPlan.TeacherProKey);
            (plan.PendingMonthlyPrice, plan.PendingMonthlyPriceFromUtc) = (plan.MonthlyPrice + 100, Now.AddDays(30));
            await ctx.SaveChangesAsync();
        }

        (await RunAsync()).ShouldBe(1);

        var e = (await EventsAsync()).ShouldHaveSingleItem();
        (e.RecipientUserId, e.StudentId, e.AssignmentId).ShouldBe((TeacherUser, (int?)null, assignmentId));
        (e.NewPrice - e.CurrentPrice).ShouldBe(100m);
    }

    [Fact]
    public void Admin_increase_waits_thirty_days_the_same_increase_is_kept_and_a_decrease_applies_now()
    {
        var now = Now;
        PurchaseTermsPolicy.Apply(100, null, null, 120, now).ShouldBe((100m, (decimal?)120m, (DateTime?)now.AddDays(30)));
        PurchaseTermsPolicy.Apply(100, 120, now.AddDays(-3), 120, now).ShouldBe((100m, (decimal?)120m, (DateTime?)now.AddDays(-3)));
        PurchaseTermsPolicy.Apply(100, 120, now.AddDays(-3), 130, now).ShouldBe((100m, (decimal?)130m, (DateTime?)now.AddDays(30)));
        PurchaseTermsPolicy.Apply(100, 120, now.AddDays(-3), 100, now).ShouldBe((100m, (decimal?)null, (DateTime?)null));
        PurchaseTermsPolicy.Apply(100, null, null, 80, now).ShouldBe((80m, (decimal?)null, (DateTime?)null));
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #441: hoşgörü süresi hatırlatması (bitişten 7 ve 1 gün önce, (öğrenci, bitiş, kalan gün) başına bir kez; Active veli / açık davet /
/// süresi dolmuş muaf) ve admin panosundaki velisiz öğrenci sayıları.
/// </summary>
public class ParentGraceReminderJobTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 15, 7, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly ParentRequirementOptions _options = new() { GraceDays = 30, FinalWarningDays = 7 };
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly HashSet<int> _unresolvable = new();

    public ParentGraceReminderJobTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Where(id => !_unresolvable.Contains(id))
                .Select(id => new UserLookupResultDto { Id = id, KeycloakId = $"kc-{id}", FullName = "Ali Kaya" }).ToList());
    }

    public void Dispose() => _db.Dispose();

    private IOptionsMonitor<ParentRequirementOptions> Monitor()
    {
        var monitor = Substitute.For<IOptionsMonitor<ParentRequirementOptions>>();
        monitor.CurrentValue.Returns(_options);
        return monitor;
    }

    private ParentGraceReminderJob Job(AppDbContext ctx) => new(ctx, _authApi, Monitor(), _time);

    private async Task<int> SeedStudentAsync(int userId, DateTime? startedAt, ParentStudentLinkStatus? link = null, bool invitation = false)
    {
        await using var ctx = _db.NewContext();
        var student = new Student { UserId = userId, StudentNumber = userId.ToString(), ParentRequirementStartedAt = startedAt };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        if (link is { } status)
        {
            var parent = new Parent { UserId = userId + 100_000 };
            ctx.Parents.Add(parent);
            await ctx.SaveChangesAsync();
            var l = new ParentStudentLink
            {
                ParentId = parent.Id, StudentId = student.Id, Status = status,
                Origin = invitation ? ParentStudentLinkOrigin.StudentRegistered : ParentStudentLinkOrigin.LegacyV1, CreatedAt = Now
            };
            ctx.ParentStudentLinks.Add(l);
            await ctx.SaveChangesAsync();
            if (invitation)
            {
                ctx.ParentInvitations.Add(new ParentInvitation
                {
                    Id = Guid.NewGuid(), StudentId = student.Id, ParentId = parent.Id, ParentUserId = parent.UserId, ParentKeycloakId = "kc",
                    LinkId = l.Id, Email = $"v{userId}@example.com", FullName = "Veli", Phone = "+905321234567",
                    Status = ParentInvitationStatus.Pending, ExpiresAt = Now.AddDays(8), SendCount = 1, LastSentAt = Now, CreatedAt = Now
                });
                await ctx.SaveChangesAsync();
            }
        }
        return student.Id;
    }

    private List<StudentParentGraceReminderEvent> Events()
    {
        using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<StudentParentGraceReminderEvent>();
        return ctx.OutboxMessages.Where(m => m.Type == type).AsEnumerable()
            .Select(m => JsonSerializer.Deserialize<StudentParentGraceReminderEvent>(m.Content)!).ToList();
    }

    [Fact]
    public async Task Seven_and_one_day_reminders_are_written_once_each()
    {
        var studentId = await SeedStudentAsync(1, Now.AddDays(-23)); // 7 gün kaldı

        await using (var ctx = _db.NewContext())
            (await Job(ctx).RunAsync()).ShouldBe(1);
        await using (var ctx = _db.NewContext())
            (await Job(ctx).RunAsync()).ShouldBe(0); // aynı gün tekrar: yok

        _time.Now = _time.Now.AddDays(6); // 1 gün kaldı
        await using (var ctx = _db.NewContext())
            (await Job(ctx).RunAsync()).ShouldBe(1);

        var events = Events();
        events.Select(e => e.DaysLeft).ShouldBe(new[] { 7, 1 }, ignoreOrder: true);
        events.ShouldAllBe(e => e.StudentId == studentId && e.GraceEndsAt == Now.AddDays(7) && e.StudentUserId == "kc-1");
        await using var check = _db.NewContext();
        (await check.StudentParentGraceReminders.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Student_whose_notification_target_cannot_be_resolved_is_retried_next_run()
    {
        await SeedStudentAsync(8, Now.AddDays(-23));
        _unresolvable.Add(8);

        await using (var ctx = _db.NewContext())
            (await Job(ctx).RunAsync()).ShouldBe(0);
        await using (var check = _db.NewContext())
            (await check.StudentParentGraceReminders.AnyAsync()).ShouldBeFalse(); // işaretçi yok

        _unresolvable.Clear();
        await using (var ctx = _db.NewContext())
            (await Job(ctx).RunAsync()).ShouldBe(1);
        Events().Single().StudentUserId.ShouldBe("kc-8");
    }

    [Fact]
    public async Task Open_invitation_suppresses_the_reminder_only_if_it_outlives_the_grace_end()
    {
        // 7 gün kaldı; davet 3 gün sonra doluyor → bitimde kilit gelebilir → hatırlatılır (review R3).
        var shortLived = await SeedStudentAsync(20, Now.AddDays(-23), ParentStudentLinkStatus.Pending, invitation: true);
        await using (var seed = _db.NewContext())
            await seed.ParentInvitations.Where(i => i.StudentId == shortLived)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, Now.AddDays(3)));
        // Davet süre bitiminden sonra da geçerli → kilit gelmez → hatırlatma yok.
        var longLived = await SeedStudentAsync(21, Now.AddDays(-23), ParentStudentLinkStatus.Pending, invitation: true);
        await using (var seed = _db.NewContext())
            await seed.ParentInvitations.Where(i => i.StudentId == longLived)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, Now.AddDays(8)));

        await using var ctx = _db.NewContext();
        (await Job(ctx).RunAsync()).ShouldBe(1);
        Events().Single().StudentId.ShouldBe(shortLived);
    }

    [Fact]
    public async Task Missed_days_send_only_the_closest_threshold()
    {
        await SeedStudentAsync(2, Now.AddDays(-29.5)); // 12 saat kaldı

        await using var ctx = _db.NewContext();
        (await Job(ctx).RunAsync()).ShouldBe(1);
        Events().Single().DaysLeft.ShouldBe(1);
    }

    [Fact]
    public async Task Exempt_far_expired_and_not_started_students_are_skipped()
    {
        await SeedStudentAsync(3, Now.AddDays(-23), ParentStudentLinkStatus.Active); // Active veli
        await SeedStudentAsync(4, Now.AddDays(-23), ParentStudentLinkStatus.Pending, invitation: true); // aktivasyon bekliyor
        await SeedStudentAsync(5, Now.AddDays(-31)); // süre doldu
        await SeedStudentAsync(6, Now.AddDays(-5)); // 25 gün kaldı
        await SeedStudentAsync(7, null); // sayaç başlamadı

        await using var ctx = _db.NewContext();
        (await Job(ctx).RunAsync()).ShouldBe(0);
        Events().ShouldBeEmpty();
    }

    [Fact]
    public async Task Admin_dashboard_counts_the_target_set()
    {
        await SeedStudentAsync(10, Now.AddDays(-5)); // grace
        await SeedStudentAsync(11, Now.AddDays(-40)); // expired
        await SeedStudentAsync(12, null); // başlamadı (hedef kümede)
        await SeedStudentAsync(13, Now.AddDays(-5), ParentStudentLinkStatus.Active); // muaf
        await SeedStudentAsync(14, null, ParentStudentLinkStatus.Pending, invitation: true); // aktivasyon bekliyor

        await using var ctx = _db.NewContext();
        DashboardSummaryDto summary = await new DashboardService(ctx, parentRequirement: Monitor(), time: _time).GetSummaryAsync();

        summary.StudentsWithoutParent.ShouldBe(3); // Grace (10, 12 başlamadı) + Expired (11)
        summary.StudentsInParentGrace.ShouldBe(2);
        summary.StudentsParentGraceExpired.ShouldBe(1);
        summary.StudentsAwaitingParentActivation.ShouldBe(1);
    }
}

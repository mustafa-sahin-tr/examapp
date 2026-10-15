using ExamApp.Api.Data;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #441 (+#440 modları): öğrenci veli durumu — sayaç ilk girişte (profil tazeleme) başlar, Active veli damgayı temizler, açık davet
/// sayacı başlatmaz ve AwaitingActivation verir; Grace kalan gün / son 7 gün uyarısı; süre bitince Expired; GraceDays=0 anında Expired;
/// muaf küme (veli kuralına tabi değil / öğrenci kaydı yok / Active veli); önbellek ve geçersizleme. Hatırlatma eşikleri + davet sınırları.
/// </summary>
public class StudentParentStatusServiceTests : IDisposable
{
    private const int StudentUser = 6001;
    private static readonly DateTime Now = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ParentRequirementOptions _options = new() { GraceDays = 30, FinalWarningDays = 7, StatusCacheSeconds = 0 };
    private int _studentId;

    public StudentParentStatusServiceTests()
    {
        using var ctx = _db.NewContext();
        var student = new Student { UserId = StudentUser, StudentNumber = "1" };
        ctx.Students.Add(student);
        ctx.SaveChanges();
        _studentId = student.Id;
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
    }

    private StudentParentStatusService Service(AppDbContext ctx)
    {
        var monitor = Substitute.For<IOptionsMonitor<ParentRequirementOptions>>();
        monitor.CurrentValue.Returns(_ => _options);
        return new StudentParentStatusService(ctx, new StudentParentStatusCache(_cache), monitor, _time);
    }

    private async Task<DateTime?> StartedAtAsync()
    {
        await using var ctx = _db.NewContext();
        return (await ctx.Students.SingleAsync(s => s.Id == _studentId)).ParentRequirementStartedAt;
    }

    private async Task SeedLinkAsync(ParentStudentLinkStatus status, ParentStudentLinkOrigin origin, bool withInvitation = false)
    {
        await using var ctx = _db.NewContext();
        var parent = new Parent { UserId = 8000 + Random.Shared.Next(1, 999) };
        ctx.Parents.Add(parent);
        await ctx.SaveChangesAsync();
        var link = new ParentStudentLink { ParentId = parent.Id, StudentId = _studentId, Status = status, Origin = origin, CreatedAt = Now };
        ctx.ParentStudentLinks.Add(link);
        await ctx.SaveChangesAsync();
        if (withInvitation)
        {
            ctx.ParentInvitations.Add(new ParentInvitation
            {
                Id = Guid.NewGuid(), StudentId = _studentId, ParentId = parent.Id, ParentUserId = parent.UserId, ParentKeycloakId = "kc",
                LinkId = link.Id, Email = "v@example.com", FullName = "Veli", Phone = "+905321234567", Status = ParentInvitationStatus.Pending,
                ExpiresAt = Now.AddDays(7), SendCount = 1, LastSentAt = Now, CreatedAt = Now, IsNewAccount = true
            });
            await ctx.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task First_refresh_starts_the_grace_counter_and_later_refreshes_keep_it()
    {
        await using (var ctx = _db.NewContext())
        {
            var state = await Service(ctx).RefreshAndStampAsync(StudentUser, requiresParent: true);
            state.Mode.ShouldBe(StudentParentMode.Grace);
            state.DaysLeft.ShouldBe(30);
            state.GraceEndsAt.ShouldBe(Now.AddDays(30));
            state.IsFinalWarning.ShouldBeFalse();
        }
        (await StartedAtAsync()).ShouldBe(Now);

        _time.Now = _time.Now.AddDays(24);
        await using (var ctx = _db.NewContext())
        {
            var state = await Service(ctx).RefreshAndStampAsync(StudentUser, requiresParent: true);
            state.DaysLeft.ShouldBe(6);
            state.IsFinalWarning.ShouldBeTrue();
        }
        (await StartedAtAsync()).ShouldBe(Now); // damga değişmez

        _time.Now = _time.Now.AddDays(6);
        await using (var ctx = _db.NewContext())
        {
            var state = await Service(ctx).GetAsync(StudentUser, requiresParent: true);
            state.Mode.ShouldBe(StudentParentMode.Expired);
            state.DaysLeft.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Get_without_stamp_never_writes()
    {
        await using var ctx = _db.NewContext();
        (await Service(ctx).GetAsync(StudentUser, requiresParent: true)).Mode.ShouldBe(StudentParentMode.Grace);
        (await StartedAtAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task Grace_days_zero_expires_immediately()
    {
        _options.GraceDays = 0;
        await using var ctx = _db.NewContext();
        (await Service(ctx).RefreshAndStampAsync(StudentUser, requiresParent: true)).Mode.ShouldBe(StudentParentMode.Expired);
    }

    [Fact]
    public async Task Open_invitation_means_awaiting_activation_and_the_counter_still_starts()
    {
        await SeedLinkAsync(ParentStudentLinkStatus.Pending, ParentStudentLinkOrigin.StudentRegistered, withInvitation: true);

        await using var ctx = _db.NewContext();
        var state = await Service(ctx).RefreshAndStampAsync(StudentUser, requiresParent: true);
        state.Mode.ShouldBe(StudentParentMode.AwaitingActivation);
        state.Facts!.Invitation.ShouldNotBeNull();
        (await StartedAtAsync()).ShouldBe(Now); // review M3: davet beklerken de hoşgörü işler
    }

    private static StudentParentFacts Facts(DateTime? startedAt, DateTime? invitationExpiresAt) => new(
        1, false, startedAt,
        invitationExpiresAt is DateTime e ? new OpenParentInvitationFacts(Guid.NewGuid(), "v@example.com", "Veli", e, true, Now, 1, Now) : null,
        Array.Empty<ParentEmailAttemptFact>());

    [Theory]
    // hoşgörü sürüyor, davet sürüyor → AwaitingActivation
    [InlineData(-5, 3, "AwaitingActivation")]
    // hoşgörü sürüyor, davet doldu → yine AwaitingActivation (davet özetinde expired)
    [InlineData(-5, -1, "AwaitingActivation")]
    // hoşgörü bitti, davet sürüyor → AwaitingActivation (yalnız token ömrü kadar)
    [InlineData(-40, 3, "AwaitingActivation")]
    // hoşgörü bitti, davet doldu → Expired
    [InlineData(-40, -1, "Expired")]
    // hoşgörü bitti, davet yok → Expired
    [InlineData(-40, null, "Expired")]
    // hoşgörü sürüyor, davet yok → Grace
    [InlineData(-5, null, "Grace")]
    public void Mode_rule(int startedDaysAgo, int? invitationExpiresInDays, string mode)
    {
        var state = StudentParentStatusService.Evaluate(
            Facts(Now.AddDays(startedDaysAgo), invitationExpiresInDays is int d ? Now.AddDays(d) : null), true, Now, _options);
        state.Mode.ToString().ShouldBe(mode);
    }

    [Fact]
    public async Task Invitation_whose_link_was_cancelled_is_not_open()
    {
        await SeedLinkAsync(ParentStudentLinkStatus.Revoked, ParentStudentLinkOrigin.StudentRegistered, withInvitation: true);

        await using var ctx = _db.NewContext();
        (await Service(ctx).GetAsync(StudentUser, requiresParent: true)).Mode.ShouldBe(StudentParentMode.Grace);
    }

    [Fact]
    public async Task Active_parent_is_exempt_and_clears_the_counter()
    {
        await using (var ctx = _db.NewContext())
            await Service(ctx).RefreshAndStampAsync(StudentUser, requiresParent: true);
        (await StartedAtAsync()).ShouldNotBeNull();

        await SeedLinkAsync(ParentStudentLinkStatus.Active, ParentStudentLinkOrigin.LegacyV1);

        await using (var ctx = _db.NewContext())
            (await Service(ctx).RefreshAndStampAsync(StudentUser, requiresParent: true)).Mode.ShouldBe(StudentParentMode.Ok);
        (await StartedAtAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task Exempt_set_non_requiring_account_or_no_student_row_is_ok()
    {
        await using var ctx = _db.NewContext();
        (await Service(ctx).RefreshAndStampAsync(StudentUser, requiresParent: false)).Mode.ShouldBe(StudentParentMode.Ok);
        (await Service(ctx).RefreshAndStampAsync(99_999, requiresParent: true)).Mode.ShouldBe(StudentParentMode.Ok);
        (await StartedAtAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task Cached_facts_are_used_until_invalidated()
    {
        _options.StatusCacheSeconds = 60;
        await using var ctx = _db.NewContext();
        var service = Service(ctx);
        (await service.GetAsync(StudentUser, true)).Mode.ShouldBe(StudentParentMode.Grace);

        await SeedLinkAsync(ParentStudentLinkStatus.Active, ParentStudentLinkOrigin.LegacyV1);
        (await service.GetAsync(StudentUser, true)).Mode.ShouldBe(StudentParentMode.Grace); // önbellek

        service.Invalidate(StudentUser);
        (await service.GetAsync(StudentUser, true)).Mode.ShouldBe(StudentParentMode.Ok);
    }

    // ---- saf kurallar -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10.0, null)]
    [InlineData(7.0, 7)]
    [InlineData(2.5, 7)]
    [InlineData(1.0, 1)]
    [InlineData(0.2, 1)]
    [InlineData(0.0, null)]
    public void Reminder_threshold(double daysRemaining, int? expected)
        => ParentGraceReminderJob.ThresholdFor(TimeSpan.FromDays(daysRemaining), 7).ShouldBe(expected);

    [Fact]
    public void Resend_decision_counts_all_sends_to_the_same_email()
    {
        ParentInvitationLimits.Resend(0, null, Now).ShouldBe(new ParentResendDecision(true, null, 5));
        ParentInvitationLimits.Resend(1, Now, Now.AddHours(23)).ShouldBe(new ParentResendDecision(false, Now.AddHours(24), 5));
        ParentInvitationLimits.Resend(1, Now, Now.AddHours(24)).Allowed.ShouldBeTrue();
        ParentInvitationLimits.Resend(6, Now, Now.AddDays(10)).ShouldBe(new ParentResendDecision(false, null, 0));
    }

    [Fact]
    public void Email_change_lock_after_three_distinct_emails_in_an_hour()
    {
        var attempts = new[]
        {
            new ParentEmailAttemptFact("a", Now.AddMinutes(-50)),
            new ParentEmailAttemptFact("b", Now.AddMinutes(-30)),
            new ParentEmailAttemptFact("a", Now.AddMinutes(-20)),
            new ParentEmailAttemptFact("c", Now.AddMinutes(-10)),
        };
        // E-posta başına son deneme: c −10, a −20, b −30 → 3. en yeni (b) + 60 dk.
        ParentInvitationLimits.EmailChangeLockedUntil(attempts, "d", Now).ShouldBe(Now.AddMinutes(30));
        ParentInvitationLimits.EmailChangeLockedUntil(attempts, "b", Now).ShouldBeNull(); // daha önce denenmiş
        ParentInvitationLimits.EmailChangeLockedUntil(attempts.Take(2), "d", Now).ShouldBeNull();
        ParentInvitationLimits.EmailChangeLockedUntil(attempts, "d", Now.AddMinutes(31)).ShouldBeNull();
    }

    [Fact]
    public void Activation_tokens_are_random_url_safe_and_hashed()
    {
        var a = ParentActivationTokens.Generate();
        var b = ParentActivationTokens.Generate();
        a.ShouldNotBe(b);
        a.Length.ShouldBe(43);
        a.ShouldMatch("^[A-Za-z0-9_-]+$");
        ParentActivationTokens.Hash(a)!.Length.ShouldBe(64);
        ParentActivationTokens.Hash(a).ShouldBe(ParentActivationTokens.Hash(a));
        ParentActivationTokens.Hash("short").ShouldBeNull();
        ParentActivationTokens.Hash("bad token with spaces 0123456789").ShouldBeNull();
        ParentActivationTokens.Hash(null).ShouldBeNull();
    }
}

using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// issue #439 — veli onayı bildirimleri: BookingParentApprovalRequested (her veliye), BookingParentDecision (öğrenci + diğer
/// veliler / Expired'da tüm veliler) ve BookingDecisionConsumer'ın veli fan-out'u. Alıcı başına deterministik idempotency ve
/// boş KeycloakId fallback'i doğrulanır.
/// </summary>
public class BookingParentApprovalConsumersTests : IDisposable
{
    private const int ParentA = 9101;
    private const int ParentB = 9102;
    private const int StudentUser = 9001;
    private const string SubA = "kc-parent-a";
    private const string SubB = "kc-parent-b";
    private const string StudentSub = "kc-student-1";

    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private static IHubContext<BadgeNotificationHub> NewHub()
    {
        var hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
        hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return hub;
    }

    private BookingParentApprovalRequestedConsumer RequestedConsumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<BookingParentApprovalRequestedConsumer>.Instance);

    private BookingParentDecisionConsumer DecisionConsumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<BookingParentDecisionConsumer>.Instance);

    private BookingDecisionConsumer TeacherDecisionConsumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, NullLogger<BookingDecisionConsumer>.Instance,
        FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance);

    private static List<BookingParentRecipient> TwoParents(string subA = SubA, string subB = SubB) => new()
    {
        new() { ParentUserId = ParentA, KeycloakId = subA },
        new() { ParentUserId = ParentB, KeycloakId = subB }
    };

    private static BookingParentApprovalRequestedEvent Requested(List<BookingParentRecipient>? parents = null) => new()
    {
        EventId = Guid.NewGuid(),
        BookingId = 55,
        StudentId = 21,
        StudentUserId = StudentUser,
        StudentName = "Ayşe K.",
        TeacherId = 10,
        TeacherName = "Mehmet Hoca",
        Date = new DateOnly(2026, 6, 16),
        StartTime = new TimeOnly(14, 0),
        EndTime = new TimeOnly(15, 0),
        Parents = parents ?? TwoParents(),
        RequestedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(7)
    };

    private static BookingParentDecisionEvent ParentDecision(string decision, int? decidedBy = ParentA, string? note = null) => new()
    {
        EventId = Guid.NewGuid(),
        BookingId = 55,
        Decision = decision,
        StudentId = 21,
        StudentUserId = StudentUser,
        StudentKeycloakId = StudentSub,
        StudentName = "Ayşe K.",
        Parents = TwoParents(),
        DecidedByParentUserId = decision == BookingParentDecisions.Expired ? null : decidedBy,
        DecidedByParentName = decision == BookingParentDecisions.Expired ? null : "Fatma Y.",
        TeacherId = 10,
        TeacherName = "Mehmet Hoca",
        Date = new DateOnly(2026, 6, 16),
        StartTime = new TimeOnly(14, 0),
        EndTime = new TimeOnly(15, 0),
        Note = note,
        DecidedAt = DateTime.UtcNow
    };

    private static BookingDecisionEvent TeacherDecision(bool approved = true, bool unavailable = false, List<BookingParentRecipient>? parents = null) => new()
    {
        BookingId = 55,
        TeacherId = 10,
        TeacherName = "Mehmet Hoca",
        StudentId = 21,
        StudentUserId = StudentUser,
        StudentName = "Ayşe K.",
        TargetKeycloakId = StudentSub,
        Approved = approved,
        TeacherUnavailable = unavailable,
        RejectionReason = !approved && !unavailable ? "Uygun değilim" : null,
        Date = new DateOnly(2026, 6, 16),
        StartTime = new TimeOnly(14, 0),
        EndTime = new TimeOnly(15, 0),
        DecidedAt = DateTime.UtcNow,
        Parents = parents ?? TwoParents()
    };

    // ---- BookingParentApprovalRequested ----------------------------------------------------------------------------

    [Fact]
    public async Task Requested_NotifiesEachParent_WithTextAndStudentLinkData()
    {
        var hub = NewHub();
        var e = Requested();

        await RequestedConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var rows = await check.Notifications.OrderBy(n => n.UserId).ToListAsync();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(n => n.Type == BookingParentApprovalRequestedConsumer.NotificationType);
        rows[0].UserId.ShouldBe(ParentA);
        rows[0].UserKeycloakId.ShouldBe(SubA);
        rows[1].UserId.ShouldBe(ParentB);
        rows[1].UserKeycloakId.ShouldBe(SubB);
        rows[0].SourceEventId.ShouldNotBe(rows[1].SourceEventId); // alıcı başına ayrı anahtar
        rows[0].Body.ShouldContain("Ayşe K.");
        rows[0].Body.ShouldContain("Mehmet Hoca");
        rows[0].Body.ShouldContain("16.06.2026 14:00-15:00");
        rows[0].Body.ShouldContain("onayınız bekleniyor");
        rows[0].Data!.ShouldContain("\"studentId\":21");
        hub.Clients.Received(1).User(SubA);
        hub.Clients.Received(1).User(SubB);
    }

    [Fact]
    public async Task Requested_Redelivery_WritesNothingNew()
    {
        var hub = NewHub();
        var e = Requested();

        await RequestedConsumer(hub).Consume(Context(e));
        await RequestedConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
        hub.Clients.Received(1).User(SubA);
        hub.Clients.Received(1).User(SubB);
    }

    [Fact]
    public async Task Requested_EmptyKeycloakId_ResolvesSubFromLocalePreference()
    {
        await using (var seed = _db.NewContext())
        {
            seed.UserLocalePreferences.Add(new UserLocalePreference { UserId = ParentA, KeycloakId = SubA, UpdatedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        var hub = NewHub();

        await RequestedConsumer(hub).Consume(Context(Requested(TwoParents(subA: "", subB: SubB))));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync(n => n.UserId == ParentA)).UserKeycloakId.ShouldBe(SubA);
        hub.Clients.Received(1).User(SubA);
    }

    [Fact]
    public async Task Requested_UnresolvableSub_WritesOthersThenThrows_ReplayWritesOnlyMissing()
    {
        var hub = NewHub();
        var e = Requested(TwoParents(subA: "", subB: SubB));

        await Should.ThrowAsync<RecipientSubUnresolvedException>(() => RequestedConsumer(hub).Consume(Context(e)));
        await using (var mid = _db.NewContext())
        {
            (await mid.Notifications.SingleAsync()).UserId.ShouldBe(ParentB); // çözülebilen yazıldı, eksik sessizce düşmedi
        }

        await using (var seed = _db.NewContext())
        {
            seed.UserLocalePreferences.Add(new UserLocalePreference { UserId = ParentA, KeycloakId = SubA, UpdatedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        await RequestedConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
        hub.Clients.Received(1).User(SubB); // B tekrar push edilmedi
    }

    // ---- BookingParentDecision -------------------------------------------------------------------------------------

    [Fact]
    public async Task Decision_Approved_NotifiesStudent_AndOtherParentOnly()
    {
        var hub = NewHub();
        var e = ParentDecision(BookingParentDecisions.Approved, decidedBy: ParentA);

        await DecisionConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var rows = await check.Notifications.ToListAsync();
        rows.Count.ShouldBe(2);

        var student = rows.Single(n => n.UserId == StudentUser);
        student.Type.ShouldBe(BookingParentDecisionConsumer.StudentApprovedType);
        student.UserKeycloakId.ShouldBe(StudentSub);
        student.SourceEventId.ShouldBe(e.EventId);
        student.Body.ShouldContain("onayladı");
        student.Body.ShouldContain("öğretmenin onayı bekleniyor");

        var other = rows.Single(n => n.UserId == ParentB); // kararı veren ParentA'ya bildirim yok
        other.Type.ShouldBe(BookingParentDecisionConsumer.ParentApprovedByOtherType);
        other.Body.ShouldContain("Fatma Y.");
        other.Data!.ShouldContain("\"studentId\":21");
        rows.ShouldNotContain(n => n.UserId == ParentA);
    }

    [Fact]
    public async Task Decision_Rejected_StudentGetsNote_OtherParentNotified()
    {
        var hub = NewHub();
        var e = ParentDecision(BookingParentDecisions.Rejected, decidedBy: ParentB, note: "Sınav haftası");

        await DecisionConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var student = await check.Notifications.SingleAsync(n => n.UserId == StudentUser);
        student.Type.ShouldBe(BookingParentDecisionConsumer.StudentRejectedType);
        student.Body.ShouldContain("Sınav haftası");
        var other = await check.Notifications.SingleAsync(n => n.UserId == ParentA);
        other.Type.ShouldBe(BookingParentDecisionConsumer.ParentRejectedByOtherType);
        (await check.Notifications.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Decision_Expired_NotifiesStudentAndAllParents()
    {
        var hub = NewHub();

        await DecisionConsumer(hub).Consume(Context(ParentDecision(BookingParentDecisions.Expired)));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(3);
        (await check.Notifications.SingleAsync(n => n.UserId == StudentUser)).Type.ShouldBe(BookingParentDecisionConsumer.StudentExpiredType);
        (await check.Notifications.CountAsync(n => n.Type == BookingParentDecisionConsumer.ParentExpiredType)).ShouldBe(2);
    }

    [Fact]
    public async Task Decision_Redelivery_WritesNothingNew_AndCompletesMissingFanOut()
    {
        var hub = NewHub();
        var e = ParentDecision(BookingParentDecisions.Approved, decidedBy: ParentA);

        await DecisionConsumer(hub).Consume(Context(e));
        await DecisionConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
        hub.Clients.Received(1).User(StudentSub);
        hub.Clients.Received(1).User(SubB);
    }

    [Fact]
    public async Task Decision_EmptyParentSub_ThrowsAfterStudentWritten_ReplayCompletes()
    {
        var hub = NewHub();
        var e = ParentDecision(BookingParentDecisions.Approved, decidedBy: ParentA);
        e.Parents = TwoParents(subB: "");

        await Should.ThrowAsync<RecipientSubUnresolvedException>(() => DecisionConsumer(hub).Consume(Context(e)));
        await using (var mid = _db.NewContext())
        {
            (await mid.Notifications.SingleAsync()).UserId.ShouldBe(StudentUser);
        }

        await using (var seed = _db.NewContext())
        {
            seed.UserLocalePreferences.Add(new UserLocalePreference { UserId = ParentB, KeycloakId = SubB, UpdatedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        await DecisionConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
        hub.Clients.Received(1).User(StudentSub); // öğrenci tekrar push edilmedi
    }

    [Fact]
    public async Task Decision_UnresolvableStudentSub_StillNotifiesParents_ThenThrows()
    {
        var e = ParentDecision(BookingParentDecisions.Approved, decidedBy: ParentA);
        e.StudentKeycloakId = "";

        await Should.ThrowAsync<RecipientSubUnresolvedException>(() => DecisionConsumer(NewHub()).Consume(Context(e)));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).UserId.ShouldBe(ParentB);
    }

    [Fact]
    public async Task TeacherDecision_UnresolvableStudentSub_StillNotifiesParents_ThenThrows()
    {
        var e = TeacherDecision(approved: true);
        e.TargetKeycloakId = "";

        await Should.ThrowAsync<RecipientSubUnresolvedException>(() => TeacherDecisionConsumer(NewHub()).Consume(Context(e)));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.Type == BookingDecisionConsumer.ParentApprovedType)).ShouldBe(2);
        (await check.Notifications.CountAsync(n => n.UserId == StudentUser)).ShouldBe(0);
    }

    [Fact]
    public async Task Decision_UnknownDecision_Throws()
    {
        var e = ParentDecision("Bogus");
        await Should.ThrowAsync<InvalidOperationException>(() => DecisionConsumer(NewHub()).Consume(Context(e)));
    }

    // ---- BookingDecisionConsumer veli fan-out ----------------------------------------------------------------------

    [Fact]
    public async Task TeacherDecision_Approved_NotifiesStudentAndEachParent()
    {
        var hub = NewHub();
        var e = TeacherDecision(approved: true);

        await TeacherDecisionConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(3);
        (await check.Notifications.SingleAsync(n => n.UserId == StudentUser)).Type.ShouldBe(BookingDecisionConsumer.ApprovedType);
        var parents = await check.Notifications.Where(n => n.Type == BookingDecisionConsumer.ParentApprovedType).ToListAsync();
        parents.Select(n => n.UserId).OrderBy(x => x).ShouldBe(new[] { ParentA, ParentB });
        parents[0].Body.ShouldContain("Mehmet Hoca");
        parents[0].Body.ShouldContain("Ayşe K.");
        parents[0].Data!.ShouldContain("\"studentId\":21");
    }

    [Fact]
    public async Task TeacherDecision_TeacherUnavailable_ParentsGetUnavailableText()
    {
        var hub = NewHub();

        await TeacherDecisionConsumer(hub).Consume(Context(TeacherDecision(approved: false, unavailable: true)));

        await using var check = _db.NewContext();
        var parents = await check.Notifications.Where(n => n.Type == BookingDecisionConsumer.ParentRejectedType).ToListAsync();
        parents.Count.ShouldBe(2);
        parents.ShouldAllBe(n => n.Body.Contains("müsait olmadığı") && n.Body.Contains("Ayşe K."));
    }

    [Fact]
    public async Task TeacherDecision_EmptyStudentName_FallsBackToDefaultChild()
    {
        var e = TeacherDecision(approved: true);
        e.StudentName = "";

        await TeacherDecisionConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.FirstAsync(n => n.Type == BookingDecisionConsumer.ParentApprovedType)).Body.ShouldContain("Çocuğunuz");
    }

    [Fact]
    public async Task TeacherDecision_Redelivery_IsIdempotent_AndEmptyParentsKeepsOldBehavior()
    {
        var hub = NewHub();
        var e = TeacherDecision(approved: false);

        await TeacherDecisionConsumer(hub).Consume(Context(e));
        await TeacherDecisionConsumer(hub).Consume(Context(e));

        await using (var check = _db.NewContext())
        {
            (await check.Notifications.CountAsync()).ShouldBe(3);
            (await check.Notifications.SingleAsync(n => n.Type == BookingDecisionConsumer.ParentRejectedType && n.UserId == ParentA))
                .Body.ShouldContain("Uygun değilim");
        }
        hub.Clients.Received(1).User(SubA);

        var legacy = TeacherDecision(approved: true, parents: new());
        legacy.BookingId = 99;
        await TeacherDecisionConsumer(hub).Consume(Context(legacy));
        await using var after = _db.NewContext();
        (await after.Notifications.CountAsync(n => n.SourceBookingId == 99)).ShouldBe(1); // yalnız öğrenci
    }
}

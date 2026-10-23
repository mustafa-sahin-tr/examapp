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
/// issue #315 — randevu iptal / erteleme / no-show bildirimleri (<see cref="BookingLifecycleEvent"/>):
/// alıcı seçimi (NotifyTeacher / NotifyStudent), tür adları (<c>Booking{Kind}</c>), veli kopyaları yalnız iptal / kabul edilen
/// erteleme / no-show için (<c>ParentBooking{Kind}</c>), no-show metni tarafa göre, gerekçe eki, idempotent yeniden teslim,
/// bilinmeyen Kind hatası ve sub çözülemeyen alıcı davranışı.
/// </summary>
public class BookingLifecycleConsumerTests : IDisposable
{
    private const int TeacherUser = 9201;
    private const int StudentUser = 9001;
    private const int ParentA = 9101;
    private const int ParentB = 9102;
    private const string TeacherSub = "kc-teacher";
    private const string StudentSub = "kc-student";
    private const string SubA = "kc-parent-a";
    private const string SubB = "kc-parent-b";

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

    private BookingLifecycleConsumer Consumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<BookingLifecycleConsumer>.Instance);

    private static List<BookingParentRecipient> TwoParents() =>
    [
        new() { ParentUserId = ParentA, KeycloakId = SubA },
        new() { ParentUserId = ParentB, KeycloakId = SubB }
    ];

    /// <summary>Varsayılan olay: öğrenci tarafından, 25.06.2026 10:00-11:00 randevu; alıcılar ve veliler configure ile değişir.</summary>
    private static BookingLifecycleEvent Lifecycle(string kind, Action<BookingLifecycleEvent>? configure = null)
    {
        var e = new BookingLifecycleEvent
        {
            EventId = Guid.NewGuid(),
            BookingId = 55,
            Kind = kind,
            ActorRole = "Student",
            TeacherId = 10,
            TeacherUserId = TeacherUser,
            TeacherName = "Mehmet Hoca",
            TeacherKeycloakId = TeacherSub,
            StudentId = 21,
            StudentUserId = StudentUser,
            StudentName = "Ayşe K.",
            StudentKeycloakId = StudentSub,
            Date = new DateOnly(2026, 6, 25),
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 0),
            Parents = TwoParents(),
            OccurredAt = DateTime.UtcNow
        };
        configure?.Invoke(e);
        return e;
    }

    private static void ToTeacher(BookingLifecycleEvent e)
    {
        e.ActorRole = "Student";
        e.NotifyTeacher = true;
        e.NotifyStudent = false;
    }

    private static void ToStudent(BookingLifecycleEvent e)
    {
        e.ActorRole = "Teacher";
        e.NotifyTeacher = false;
        e.NotifyStudent = true;
    }

    private static void WithNewTime(BookingLifecycleEvent e, TimeOnly start, TimeOnly end)
    {
        e.NewDate = new DateOnly(2026, 6, 25);
        e.NewStartTime = start;
        e.NewEndTime = end;
    }

    private async Task<List<Notification>> AllAsync()
    {
        await using var check = _db.NewContext();
        return await check.Notifications.OrderBy(n => n.UserId).ThenBy(n => n.Type).ToListAsync();
    }

    // ---- Alıcı seçimi ve tür adları -------------------------------------------------------------------------------

    [Fact]
    public async Task Cancelled_ByStudent_NotifiesTeacherAndParents_WithTypedTexts()
    {
        var hub = NewHub();
        var e = Lifecycle("Cancelled", c => ToTeacher(c));

        await Consumer(hub).Consume(Context(e));

        var rows = await AllAsync();
        rows.Count.ShouldBe(3);

        var teacher = rows.Single(n => n.UserId == TeacherUser);
        teacher.Type.ShouldBe("BookingCancelled");
        teacher.UserKeycloakId.ShouldBe(TeacherSub);
        teacher.Body.ShouldContain("Ayşe K.");
        teacher.Body.ShouldContain("25.06.2026 10:00-11:00");
        teacher.Body.ShouldContain("iptal etti");
        teacher.SourceEventId.ShouldNotBe(Guid.Empty); // alıcı başına deterministik anahtar (booking-lifecycle:{EventId} + alıcı)

        var parents = rows.Where(n => n.Type == "ParentBookingCancelled").ToList();
        parents.Select(n => n.UserId).OrderBy(x => x).ShouldBe(new[] { ParentA, ParentB });
        parents.ShouldAllBe(n => n.Body.Contains("Ayşe K.") && n.Body.Contains("Mehmet Hoca") && n.Body.Contains("25.06.2026 10:00-11:00"));
        parents.ShouldAllBe(n => n.Data!.Contains("\"studentId\":21"));

        rows.ShouldNotContain(n => n.UserId == StudentUser);
        hub.Clients.Received(1).User(TeacherSub);
    }

    [Fact]
    public async Task Cancelled_ByTeacher_NotifiesStudentAndParents_NotTheTeacher()
    {
        var hub = NewHub();
        var e = Lifecycle("Cancelled", ToStudent);

        await Consumer(hub).Consume(Context(e));

        var rows = await AllAsync();
        rows.Count.ShouldBe(3);
        var student = rows.Single(n => n.UserId == StudentUser);
        student.Type.ShouldBe("BookingCancelled");
        student.UserKeycloakId.ShouldBe(StudentSub);
        student.Body.ShouldStartWith("Mehmet Hoca, ");
        rows.ShouldNotContain(n => n.UserId == TeacherUser);
    }

    [Fact]
    public async Task Cancelled_AwaitingParentWithdrawal_NotifiesOnlyParents()
    {
        var hub = NewHub();
        // Öğretmene hiç ulaşmamış talebin geri çekilmesi: NotifyTeacher=false, NotifyStudent=false, veliler yine bilgilenir.
        var e = Lifecycle("Cancelled", c =>
        {
            c.ActorRole = "Student";
            c.NotifyTeacher = false;
            c.NotifyStudent = false;
        });

        await Consumer(hub).Consume(Context(e));

        var rows = await AllAsync();
        rows.Select(n => n.Type).ShouldAllBe(t => t == "ParentBookingCancelled");
        rows.Count.ShouldBe(2);
        hub.Clients.DidNotReceive().User(TeacherSub);
    }

    [Fact]
    public async Task RescheduleProposed_ByStudent_NotifiesTeacherOnly_WithProposedTime()
    {
        var hub = NewHub();
        var e = Lifecycle("RescheduleProposed", c =>
        {
            ToTeacher(c);
            WithNewTime(c, new TimeOnly(14, 0), new TimeOnly(15, 0));
        });

        await Consumer(hub).Consume(Context(e));

        var rows = await AllAsync();
        var row = rows.Single(n => n.UserId == TeacherUser);
        row.Type.ShouldBe("BookingRescheduleProposed");
        row.Body.ShouldContain("Ayşe K.");
        row.Body.ShouldContain("25.06.2026 10:00-11:00");
        row.Body.ShouldContain("25.06.2026 14:00-15:00");
        rows.Count.ShouldBe(3);
    }

    [Fact]
    public async Task RescheduleProposed_ByStudent_AlsoNotifiesActiveParentsWithProposedTime()
    {
        // Güvenlik turu (6): veliler öneriyi ParentBookingRescheduleProposed ile görür.
        var hub = NewHub();
        var e = Lifecycle("RescheduleProposed", c =>
        {
            ToTeacher(c);
            WithNewTime(c, new TimeOnly(14, 0), new TimeOnly(15, 0));
        });

        await Consumer(hub).Consume(Context(e));

        var parents = (await AllAsync()).Where(n => n.Type == "ParentBookingRescheduleProposed").ToList();
        parents.Select(n => n.UserId).OrderBy(x => x).ShouldBe(new[] { ParentA, ParentB });
        parents.ShouldAllBe(n => n.Body.StartsWith("Ayşe K. ile Mehmet Hoca arasındaki"));
        parents.ShouldAllBe(n => n.Body.Contains("25.06.2026 14:00-15:00"));
    }

    [Fact]
    public async Task RescheduleDeclined_ByTeacher_NotifiesStudentOnly()
    {
        var hub = NewHub();
        var e = Lifecycle("RescheduleDeclined", c =>
        {
            ToStudent(c);
            WithNewTime(c, new TimeOnly(14, 0), new TimeOnly(15, 0));
        });

        await Consumer(hub).Consume(Context(e));

        var row = (await AllAsync()).ShouldHaveSingleItem();
        row.UserId.ShouldBe(StudentUser);
        row.Type.ShouldBe("BookingRescheduleDeclined");
        row.Body.ShouldContain("Mehmet Hoca");
    }

    [Fact]
    public async Task RescheduleWithdrawn_ByStudent_NotifiesTeacherOnly()
    {
        var hub = NewHub();
        var e = Lifecycle("RescheduleWithdrawn", c =>
        {
            ToTeacher(c);
            WithNewTime(c, new TimeOnly(14, 0), new TimeOnly(15, 0));
        });

        await Consumer(hub).Consume(Context(e));

        var row = (await AllAsync()).ShouldHaveSingleItem();
        row.UserId.ShouldBe(TeacherUser);
        row.Type.ShouldBe("BookingRescheduleWithdrawn");
    }

    [Fact]
    public async Task RescheduleAccepted_ByTeacher_NotifiesStudentAndParents_WithNewTime()
    {
        var hub = NewHub();
        var e = Lifecycle("RescheduleAccepted", c =>
        {
            ToStudent(c);
            WithNewTime(c, new TimeOnly(14, 0), new TimeOnly(15, 0));
        });

        await Consumer(hub).Consume(Context(e));

        var rows = await AllAsync();
        rows.Count.ShouldBe(3);
        var student = rows.Single(n => n.UserId == StudentUser);
        student.Type.ShouldBe("BookingRescheduleAccepted");
        student.Body.ShouldContain("25.06.2026 14:00-15:00");

        var parents = rows.Where(n => n.Type == "ParentBookingRescheduleAccepted").ToList();
        parents.Select(n => n.UserId).OrderBy(x => x).ShouldBe(new[] { ParentA, ParentB });
        parents.ShouldAllBe(n => n.Body.Contains("25.06.2026 14:00-15:00") && n.Body.Contains("taşındı"));
    }

    [Fact]
    public async Task NoShowReported_StudentReportsTeacherAbsent_UsesTeacherAbsentParentText()
    {
        var hub = NewHub();
        var e = Lifecycle("NoShowReported", c =>
        {
            ToTeacher(c);
            c.NoShowParty = "Teacher";
        });

        await Consumer(hub).Consume(Context(e));

        var rows = await AllAsync();
        var teacher = rows.Single(n => n.UserId == TeacherUser);
        teacher.Type.ShouldBe("BookingNoShowReported");
        // Tür adı ParentBooking{Kind}; katılmayan taraf metin anahtarında (ParentBookingNoShowReportedTeacher).
        var parents = rows.Where(n => n.Type == "ParentBookingNoShowReported").ToList();
        parents.Select(n => n.UserId).OrderBy(x => x).ShouldBe(new[] { ParentA, ParentB });
        parents.ShouldAllBe(n => n.Body.StartsWith("Mehmet Hoca, Ayşe K. ile"));
    }

    [Fact]
    public async Task NoShowReported_TeacherReportsStudentAbsent_UsesStudentAbsentParentText()
    {
        var hub = NewHub();
        var e = Lifecycle("NoShowReported", c =>
        {
            ToStudent(c);
            c.NoShowParty = "Student";
        });

        await Consumer(hub).Consume(Context(e));

        var rows = await AllAsync();
        var student = rows.Single(n => n.UserId == StudentUser);
        student.Type.ShouldBe("BookingNoShowReported");
        student.Body.ShouldStartWith("Mehmet Hoca, ");
        var parents = rows.Where(n => n.Type == "ParentBookingNoShowReported").ToList();
        parents.Select(n => n.UserId).OrderBy(x => x).ShouldBe(new[] { ParentA, ParentB });
        parents.ShouldAllBe(n => n.Body.StartsWith("Ayşe K., Mehmet Hoca ile"));
    }

    [Theory]
    [InlineData("RescheduleDeclined")]
    [InlineData("RescheduleWithdrawn")]
    public async Task Reschedule_Decline_Withdraw_NeverCopiesParents(string kind)
    {
        var hub = NewHub();
        var e = Lifecycle(kind, c =>
        {
            c.NotifyTeacher = true;
            c.NotifyStudent = true;
            WithNewTime(c, new TimeOnly(14, 0), new TimeOnly(15, 0));
        });

        await Consumer(hub).Consume(Context(e));

        var rows = await AllAsync();
        rows.ShouldNotContain(n => n.Type.StartsWith("ParentBooking"));
        rows.Count.ShouldBe(2); // yalnız karşı taraf (öğretmen + öğrenci)
    }

    // ---- İdempotency / hata yolları ---------------------------------------------------------------------------------

    [Fact]
    public async Task Redelivery_OfTheSameEvent_WritesNothingNew()
    {
        var hub = NewHub();
        var e = Lifecycle("Cancelled", ToTeacher);

        await Consumer(hub).Consume(Context(e));
        await Consumer(hub).Consume(Context(e));

        (await AllAsync()).Count.ShouldBe(3);
        hub.Clients.Received(1).User(TeacherSub);
        hub.Clients.Received(1).User(SubA);
        hub.Clients.Received(1).User(SubB);
    }

    [Fact]
    public async Task Two_distinct_events_for_the_same_booking_each_notify()
    {
        // Aynı randevuda erteleme öner → geri çek → yeniden öner: her olay kendi EventId'si ile bildirilir.
        var hub = NewHub();
        var first = Lifecycle("RescheduleProposed", c => { ToTeacher(c); WithNewTime(c, new TimeOnly(14, 0), new TimeOnly(15, 0)); });
        var second = Lifecycle("RescheduleProposed", c => { ToTeacher(c); WithNewTime(c, new TimeOnly(14, 0), new TimeOnly(15, 0)); });

        await Consumer(hub).Consume(Context(first));
        await Consumer(hub).Consume(Context(second));

        // Alıcı bazlı (öğretmen) iki ayrı bildirim; tekil-olay anahtarı ikinciyi yutmaz.
        var teacherRows = (await AllAsync()).Where(n => n.UserId == TeacherUser).ToList();
        teacherRows.Count.ShouldBe(2);
        teacherRows.Select(n => n.SourceEventId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task UnknownKind_Throws_AndWritesNothing()
    {
        var hub = NewHub();
        var e = Lifecycle("Teleported", ToTeacher);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => Consumer(hub).Consume(Context(e)));

        ex.Message.ShouldContain("Teleported");
        (await AllAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task UnresolvableTeacherSub_WritesOthersThenThrows()
    {
        var hub = NewHub();
        var e = Lifecycle("Cancelled", c =>
        {
            ToTeacher(c);
            c.TeacherKeycloakId = "";
        });

        await Should.ThrowAsync<RecipientSubUnresolvedException>(() => Consumer(hub).Consume(Context(e)));

        var rows = await AllAsync();
        rows.ShouldNotContain(n => n.UserId == TeacherUser);
        rows.Count.ShouldBe(2); // veliler yazıldı; eksik alıcı replay'de tamamlanır
        rows.ShouldAllBe(n => n.Type == "ParentBookingCancelled");
    }

    // ---- Metin --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reason_IsAppendedAsSuffix_ToCounterpartyAndParentTexts()
    {
        var hub = NewHub();
        var e = Lifecycle("Cancelled", c =>
        {
            ToTeacher(c);
            c.Reason = "Hasta olduğum için";
        });

        await Consumer(hub).Consume(Context(e));

        var rows = await AllAsync();
        rows.Single(n => n.UserId == TeacherUser).Body.ShouldContain("Gerekçe: Hasta olduğum için");
        rows.Where(n => n.Type == "ParentBookingCancelled").ShouldAllBe(n => n.Body.Contains("Gerekçe: Hasta olduğum için"));
    }

    [Fact]
    public async Task NoReason_ProducesNoGerekceSuffix()
    {
        var hub = NewHub();
        var e = Lifecycle("Cancelled", c =>
        {
            ToTeacher(c);
            c.Reason = null;
        });

        await Consumer(hub).Consume(Context(e));

        (await AllAsync()).ShouldAllBe(n => !n.Body.Contains("Gerekçe"));
    }

    [Fact]
    public async Task EmptyStudentName_FallsBackToDefaultStudentText()
    {
        var hub = NewHub();
        var e = Lifecycle("Cancelled", c =>
        {
            ToTeacher(c);
            c.StudentName = "";
            c.Parents = [];
        });

        await Consumer(hub).Consume(Context(e));

        (await AllAsync()).Single().Body.ShouldStartWith("Bir öğrenci, ");
    }
}

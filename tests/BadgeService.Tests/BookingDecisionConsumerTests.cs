using BadgeService;
using BadgeService.Consumers;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #96 — BookingDecisionConsumer: öğretmen randevu talebini onaylayıp/reddettiğinde
/// öğrenciye bildirim ve SignalR push. İdempotency: aynı decision teslim no-op.
/// </summary>
public class BookingDecisionConsumerTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

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

    private BookingDecisionConsumer NewConsumer(
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver? localeResolver = null,
        INotificationTextFactory? texts = null)
        => new(
            _db.NewContext(),
            hub,
            NullLogger<BookingDecisionConsumer>.Instance,
            localeResolver ?? FallbackUserLocaleResolver.Instance,
            texts ?? FallbackNotificationTextFactory.Instance);

    private static BookingDecisionEvent Evt(
        int bookingId = 1,
        int teacherId = 10,
        int studentId = 20,
        int studentUserId = 200,
        bool approved = true,
        string? teacherName = "Ayşe Öğretmen",
        string? rejectionReason = null,
        string? keycloakId = "kc-student-1")
        => new()
        {
            BookingId = bookingId,
            TeacherId = teacherId,
            TeacherName = teacherName,
            StudentId = studentId,
            StudentUserId = studentUserId,
            TargetKeycloakId = keycloakId,
            Approved = approved,
            RejectionReason = rejectionReason,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(15, 0),
            DecidedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task Consume_ApprovedDecision_CreatesApprovedNotificationAndPushesToStudent()
    {
        var hub = NewHub();
        var e = Evt(approved: true);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(BookingDecisionConsumer.ApprovedType);
        n.SourceBookingId.ShouldBe(e.BookingId);
        n.UserId.ShouldBe(e.StudentUserId);
        n.UserKeycloakId.ShouldBe(e.TargetKeycloakId);
        n.IsRead.ShouldBeFalse();
        n.Title.ShouldContain("onaylandı");
        n.Body.ShouldContain(e.TeacherName);
        n.Body.ShouldContain("onayladı");

        await hub.Clients.User(e.TargetKeycloakId!).Received(1).SendCoreAsync(
            "BookingUpdate", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_RejectedDecision_CreatesRejectedNotificationWithReason()
    {
        var hub = NewHub();
        var e = Evt(approved: false, rejectionReason: "Zaman çakışıyor.");

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(BookingDecisionConsumer.RejectedType);
        n.Title.ShouldContain("reddedildi");
        n.Body.ShouldContain("reddetti");
        n.Body.ShouldContain("Zaman çakışıyor");
    }

    // issue #300: gün aşan slotta bitiş saatine " (+1)" eklenir; normal slotta eklenmez.
    [Theory]
    [InlineData(23, 30, 0, 30, "23:30-00:30 (+1)")]
    [InlineData(14, 0, 15, 0, "14:00-15:00")]
    public async Task Consume_BodyShowsNextDayMarkerOnlyForCrossingMidnightSlot(
        int sH, int sM, int eH, int eM, string expectedRange)
    {
        var e = Evt(approved: true);
        e.Date = new DateOnly(2026, 6, 16);
        e.StartTime = new TimeOnly(sH, sM);
        e.EndTime = new TimeOnly(eH, eM);

        await NewConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Body.ShouldContain($"16.06.2026 {expectedRange}");
        if (!expectedRange.Contains("(+1)"))
            n.Body.ShouldNotContain("(+1)");
    }

    [Fact]
    public async Task Consume_RejectedDecisionWithoutReason_OmitsReasonFromBody()
    {
        var hub = NewHub();
        var e = Evt(approved: false, rejectionReason: null);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(BookingDecisionConsumer.RejectedType);
        n.Body.ShouldNotContain("Gerekçe:");
    }

    [Fact]
    public async Task Consume_SameApprovedDecisionTwice_OnlyCreatesOneApprovedNotification()
    {
        var e = Evt(bookingId: 1, approved: true);

        await NewConsumer(NewHub()).Consume(Context(e));
        await NewConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(
            n => n.Type == BookingDecisionConsumer.ApprovedType && n.SourceBookingId == e.BookingId))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Consume_SameRejectedDecisionTwice_OnlyCreatesOneRejectedNotification()
    {
        var e = Evt(bookingId: 1, approved: false);

        await NewConsumer(NewHub()).Consume(Context(e));
        await NewConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(
            n => n.Type == BookingDecisionConsumer.RejectedType && n.SourceBookingId == e.BookingId))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_DoesNotPushToStudentAgain()
    {
        var e = Evt();

        await NewConsumer(NewHub()).Consume(Context(e));

        var secondHub = NewHub();
        await NewConsumer(secondHub).Consume(Context(e));

        // Second delivery should not push (idempotent no-op)
        await secondHub.Clients.User(Arg.Any<string>()).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_MissingTeacherName_FallsBackToGenericLabel()
    {
        var hub = NewHub();
        var e = Evt(teacherName: "");

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Body.ShouldContain("Öğretmeniniz");
    }

    [Fact]
    public async Task Consume_MissingKeycloakId_ResolvesSubFromLocalePreferenceAndPushes()
    {
        // Security review O2 (#298): üretici sub'ı çözemediyse BadgeService verisinden çözülür.
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new BadgeService.Entities.UserLocalePreference
            {
                UserId = 200, KeycloakId = "kc-from-locale", Locale = "tr", UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }
        var hub = NewHub();

        await NewConsumer(hub).Consume(Context(Evt(keycloakId: null)));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).UserKeycloakId.ShouldBe("kc-from-locale");
        await hub.Clients.User("kc-from-locale").Received(1).SendCoreAsync(
            "BookingUpdate", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_UnresolvableKeycloakId_ThrowsForRetryAndStoresNothing()
    {
        var hub = NewHub();

        await Should.ThrowAsync<InvalidOperationException>(() => NewConsumer(hub).Consume(Context(Evt(keycloakId: null))));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
        await hub.Clients.User(Arg.Any<string>()).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_MultipleApprovedDecisions_CreatesMultipleNotifications()
    {
        var hub = NewHub();
        var e1 = Evt(bookingId: 1, approved: true);
        var e2 = Evt(bookingId: 2, approved: true);

        await NewConsumer(hub).Consume(Context(e1));
        await NewConsumer(hub).Consume(Context(e2));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.Type == BookingDecisionConsumer.ApprovedType))
            .ShouldBe(2);
    }

    [Fact]
    public async Task Consume_NotificationDataHasApprovedFlag()
    {
        var hub = NewHub();
        var e = Evt(bookingId: 42, approved: true);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Data.ShouldContain("\"bookingId\":42");
        n.Data.ShouldContain("\"approved\":true");
    }

    [Fact]
    public async Task Consume_RejectedDecisionDataHasApprovedFalse()
    {
        var hub = NewHub();
        var e = Evt(bookingId: 43, approved: false);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Data.ShouldContain("\"approved\":false");
    }

    // ---- Issue #185: Locale-aware notification generation ----

    private IUserLocaleResolver CreateLocaleResolver()
        => new UserLocaleResolver(_db.NewContext());

    private INotificationTextFactory CreateNotificationTextFactory()
    {
        var projectRoot = GetProjectRoot();
        var resourcesPath = Path.Combine(projectRoot, "Services", "BadgeService", "Resources");
        using var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(resourcesPath);
        var store = ExamApp.Foundation.Localization.JsonResourceStore.Load(
            fileProvider, "", throwOnDuplicateKeys: false, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        return new NotificationTextFactory(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationTextFactory>.Instance);
    }

    private static string GetProjectRoot()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);

        // Walk up from bin/Debug/net10.0 to find the repository root
        while (currentDir != null)
        {
            // Look for ExamApp.slnx or Services directory
            if (File.Exists(Path.Combine(currentDir.FullName, "ExamApp.slnx")) ||
                Directory.Exists(Path.Combine(currentDir.FullName, "Services")))
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException("Could not find repository root from " + AppContext.BaseDirectory);
    }

    [Fact]
    public async Task Consume_StudentWithEnglishLocale_CreatesEnglishNotification()
    {
        // Student has English locale preference
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new BadgeService.Entities.UserLocalePreference
            {
                UserId = 200,
                KeycloakId = "kc-student-en",
                Locale = "en",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var hub = NewHub();
        var localeResolver = CreateLocaleResolver();
        var textFactory = CreateNotificationTextFactory();
        var e = Evt(studentUserId: 200, keycloakId: "kc-student-en", approved: true);

        await NewConsumer(hub, localeResolver, textFactory).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldContain("approved");
        n.Body.ShouldContain("approved");
    }

    [Fact]
    public async Task Consume_StudentWithTurkishLocale_CreatesTurkishNotification()
    {
        // Student has Turkish locale preference
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new BadgeService.Entities.UserLocalePreference
            {
                UserId = 201,
                KeycloakId = "kc-student-tr",
                Locale = "tr",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var hub = NewHub();
        var localeResolver = CreateLocaleResolver();
        var textFactory = CreateNotificationTextFactory();
        var e = Evt(studentUserId: 201, keycloakId: "kc-student-tr", approved: true);

        await NewConsumer(hub, localeResolver, textFactory).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldContain("onaylandı");
        n.Body.ShouldContain("onayladı");
    }

    [Fact]
    public async Task Consume_NoLocalePreference_DefaultsToTurkish()
    {
        var hub = NewHub();
        var localeResolver = CreateLocaleResolver();
        var textFactory = CreateNotificationTextFactory();
        var e = Evt(studentUserId: 999, keycloakId: "kc-no-pref");

        await NewConsumer(hub, localeResolver, textFactory).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        // Should be in Turkish when no preference exists
        n.Title.ShouldContain("onaylandı");
    }

    public void Dispose() => _db.Dispose();
}

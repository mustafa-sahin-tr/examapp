using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #157: TeacherApplicationDecisionConsumer — admin bir öğretmen başvurusunu onayladığında/
/// reddettiğinde başvuru sahibine in-app bildirim + SignalR push. Idempotency (Type, TeacherId) ile;
/// duplicate ikinci teslim no-op. Gerekçe/admin kimliği hiçbir alanda taşınmaz (security review).
/// </summary>
public class TeacherApplicationDecisionConsumerTests : IDisposable
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

    private TeacherApplicationDecisionConsumer NewConsumer(
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver? localeResolver = null,
        INotificationTextFactory? texts = null)
    {
        localeResolver ??= new UserLocaleResolver(_db.NewContext());
        texts ??= CreateNotificationTextFactory();
        return new(_db.NewContext(), hub, localeResolver, texts, NullLogger<TeacherApplicationDecisionConsumer>.Instance);
    }

    private static INotificationTextFactory CreateNotificationTextFactory()
    {
        var projectRoot = GetProjectRoot();
        var resourcesPath = Path.Combine(projectRoot, "Services", "BadgeService", "Resources");
        using var fileProvider = new PhysicalFileProvider(resourcesPath);
        var store = JsonResourceStore.Load(fileProvider, "", throwOnDuplicateKeys: false, NullLogger.Instance);
        return new NotificationTextFactory(store, NullLogger<NotificationTextFactory>.Instance);
    }

    private static string GetProjectRoot()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null)
        {
            if (File.Exists(Path.Combine(currentDir.FullName, "ExamApp.slnx")) ||
                Directory.Exists(Path.Combine(currentDir.FullName, "Services")))
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }
        throw new InvalidOperationException("Could not find repository root from " + AppContext.BaseDirectory);
    }

    private static TeacherApplicationDecidedEvent Evt(
        int teacherId = 1,
        bool approved = true,
        string? keycloakId = "kc-teacher",
        bool isIndependentTutor = true)
        => new()
        {
            EventId = Guid.NewGuid(),
            TeacherId = teacherId,
            TargetKeycloakId = keycloakId ?? string.Empty,
            Approved = approved,
            IsIndependentTutor = isIndependentTutor,
            DecidedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task Consume_Approved_CreatesNotificationAndPushes()
    {
        var hub = NewHub();
        var e = Evt(teacherId: 5, approved: true);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(TeacherApplicationDecisionConsumer.ApprovedType);
        n.UserKeycloakId.ShouldBe("kc-teacher");
        n.SourceTeacherApplicationId.ShouldBe(5);
        n.SourceEventId.ShouldBe(e.EventId);
        n.IsRead.ShouldBeFalse();
        // issue #157 review: UI'ın doğru sayfaya yönlendirebilmesi için IsIndependentTutor taşınır.
        n.Data.ShouldContain("isIndependentTutor");

        await hub.Clients.User("kc-teacher").Received(1).SendCoreAsync(
            "TeacherApplicationDecided", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_Rejected_CreatesNotificationWithoutReasonOrAdminIdentity()
    {
        var hub = NewHub();
        var e = Evt(teacherId: 6, approved: false);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(TeacherApplicationDecisionConsumer.RejectedType);

        // Güvenlik kararı (issue #157): payload'da/metinde gerekçe ya da admin kimliği yok.
        n.Data!.ToLowerInvariant().ShouldNotContain("reason");
        n.Data.ToLowerInvariant().ShouldNotContain("admin");
        n.Body.ShouldNotContain("Gerekçe");
        n.Body.ShouldNotContain("Reason");
    }

    [Fact]
    public async Task Consume_DuplicateEvent_OnlyCreatesOneNotification()
    {
        var hub = NewHub();
        var e = Evt(teacherId: 7);

        await NewConsumer(hub).Consume(Context(e));
        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.SourceTeacherApplicationId == 7)).ShouldBe(1);
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_DoesNotPushAgain()
    {
        var e = Evt(teacherId: 8);

        await NewConsumer(NewHub()).Consume(Context(e));

        var secondHub = NewHub();
        await NewConsumer(secondHub).Consume(Context(e));

        await secondHub.Clients.User(Arg.Any<string>()).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_ApprovedAndRejected_AreIndependentNotifications()
    {
        var hub = NewHub();
        var teacherId = 9;

        await NewConsumer(hub).Consume(Context(Evt(teacherId: teacherId, approved: true)));
        await NewConsumer(hub).Consume(Context(Evt(teacherId: teacherId, approved: false)));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.SourceTeacherApplicationId == teacherId)).ShouldBe(2);
    }

    [Fact]
    public async Task Consume_SameTeacherAndTypeWithDifferentEventId_CreatesTwoNotifications()
    {
        // issue #157 review: TeacherId+Type tek başına dedup anahtarı DEĞİL — aynı öğretmen zaman
        // içinde birden fazla kez reddedilebilir (ör. red sonrası yeni okul talebi de reddedilir).
        // Her ikisi de farklı EventId taşıdığı için iki ayrı bildirim üretilmeli.
        var hub = NewHub();
        var teacherId = 11;

        await NewConsumer(hub).Consume(Context(Evt(teacherId: teacherId, approved: false)));
        await NewConsumer(hub).Consume(Context(Evt(teacherId: teacherId, approved: false)));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.SourceTeacherApplicationId == teacherId)).ShouldBe(2);
    }

    [Fact]
    public async Task Consume_SameEventIdTwice_CreatesOnlyOneNotification()
    {
        var hub = NewHub();
        var e = Evt(teacherId: 12);

        await NewConsumer(hub).Consume(Context(e));
        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.SourceEventId == e.EventId)).ShouldBe(1);
    }

    [Fact]
    public async Task Consume_MissingKeycloakId_SkipsEntirely()
    {
        var hub = NewHub();
        var e = Evt(teacherId: 10, keycloakId: null);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.SourceTeacherApplicationId == 10)).ShouldBe(0);

        await hub.Clients.User(Arg.Any<string>()).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}

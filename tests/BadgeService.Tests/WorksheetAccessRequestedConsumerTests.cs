using BadgeService;
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
/// Issue #185 — WorksheetAccessRequestedConsumer: atama izni talebi bildirimleri
/// hedef kullanıcının tercih ettiği dilde (UserLocalePreference) üretilir.
/// İdempotency korunur: aynı RequestId ikinci teslim no-op.
/// </summary>
public class WorksheetAccessRequestedConsumerTests : IDisposable
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

    private WorksheetAccessRequestedConsumer NewConsumer(
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver? localeResolver = null,
        INotificationTextFactory? texts = null)
    {
        localeResolver ??= new UserLocaleResolver(_db.NewContext());
        texts ??= CreateNotificationTextFactory();
        return new(_db.NewContext(), hub, localeResolver, texts, NullLogger<WorksheetAccessRequestedConsumer>.Instance);
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

    private static WorksheetAccessRequestedEvent Evt(
        int requestId = 1,
        int ownerUserId = 10,
        string? requesterName = "Ahmet Hoca",
        string? worksheetName = "Kesirler Testi",
        int worksheetId = 100,
        string? keycloakId = "kc-owner")
        => new()
        {
            RequestId = requestId,
            OwnerUserId = ownerUserId,
            RequesterName = requesterName,
            WorksheetName = worksheetName,
            WorksheetId = worksheetId,
            TargetKeycloakId = keycloakId
        };

    [Fact]
    public async Task Consume_FirstDelivery_CreatesNotificationForOwner()
    {
        var hub = NewHub();
        var e = Evt();

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(WorksheetAccessRequestedConsumer.NotificationType);
        n.UserId.ShouldBe(e.OwnerUserId);
        n.SourceAccessRequestId.ShouldBe(e.RequestId);
        n.IsRead.ShouldBeFalse();
    }

    [Fact]
    public async Task Consume_OwnerWithEnglishLocale_CreatesEnglishNotification()
    {
        // Owner has English locale preference
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 10,
                KeycloakId = "kc-owner",
                Locale = "en",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var hub = NewHub();
        var e = Evt(ownerUserId: 10, keycloakId: "kc-owner");

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldContain("assignment");
        n.Title.ShouldContain("request");
    }

    [Fact]
    public async Task Consume_OwnerWithTurkishLocale_CreatesTurkishNotification()
    {
        // Owner has Turkish locale preference
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 11,
                KeycloakId = "kc-owner-tr",
                Locale = "tr",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var hub = NewHub();
        var e = Evt(ownerUserId: 11, keycloakId: "kc-owner-tr");

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldContain("atama");
        n.Title.ShouldContain("izni");
    }

    [Fact]
    public async Task Consume_NoLocalePreference_DefaultsToTurkish()
    {
        var hub = NewHub();
        var e = Evt(ownerUserId: 99);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        // Should be in Turkish when no preference exists
        n.Title.ShouldContain("atama");
    }

    [Fact]
    public async Task Consume_DuplicateEvent_OnlyCreatesOneNotification()
    {
        var hub = NewHub();
        var e = Evt(requestId: 42);

        await NewConsumer(hub).Consume(Context(e));
        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.SourceAccessRequestId == 42)).ShouldBe(1);
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_DoesNotPushAgain()
    {
        var e = Evt(requestId: 50);

        await NewConsumer(NewHub()).Consume(Context(e));

        var secondHub = NewHub();
        await NewConsumer(secondHub).Consume(Context(e));

        // Second push attempt should be no-op
        await secondHub.Clients.User(Arg.Any<string>()).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_MissingRequesterName_FallsBackToGenericLabel()
    {
        var hub = NewHub();
        var e = Evt(requesterName: "");

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        // Should contain fallback text in Turkish (no locale pref)
        n.Body.ShouldContain("Bir öğretmen");
    }

    [Fact]
    public async Task Consume_MissingKeycloakId_StoresNotificationButSkipsPush()
    {
        var hub = NewHub();
        var e = Evt(keycloakId: null);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.UserKeycloakId.ShouldBeNull();

        await hub.Clients.User(Arg.Any<string>()).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}

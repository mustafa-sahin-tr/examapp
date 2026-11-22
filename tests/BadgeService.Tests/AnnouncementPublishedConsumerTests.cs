using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// issue #1281 (#1071 S6): okul duyurusu parçası — alıcı başına bildirim (öğrenci / öğretmen / veli tipi ve metni), alıcının dili,
/// yeniden teslimde idempotency, sub'ı BadgeService verisinden çözme ve çözülemeyen alıcıyı atlayıp diğerlerini yazma.
/// </summary>
public class AnnouncementPublishedConsumerTests : IDisposable
{
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

    private static readonly Lazy<INotificationTextFactory> RealTexts = new(() =>
    {
        // Gerçek tr + en kaynakları (Fallback fabrikası dili tr'ye kilitler; alıcının dili burada sınanır).
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Services", "BadgeService", "Resources")))
            root = root.Parent;
        using var files = new PhysicalFileProvider(Path.Combine(root!.FullName, "Services", "BadgeService", "Resources"));
        var store = JsonResourceStore.Load(files, "", throwOnDuplicateKeys: false, NullLogger.Instance);
        return new NotificationTextFactory(store, NullLogger<NotificationTextFactory>.Instance);
    });

    private AnnouncementPublishedConsumer Consumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, RealTexts.Value, NullLogger<AnnouncementPublishedConsumer>.Instance);

    private static AnnouncementRecipient R(string audience, int userId, string sub = "", int? studentId = null, string child = "") => new()
    {
        NotificationId = Guid.NewGuid(), Audience = audience, UserId = userId, KeycloakId = sub, StudentId = studentId,
        StudentDisplayName = child
    };

    private static AnnouncementPublishedEvent Evt(params AnnouncementRecipient[] recipients) => new()
    {
        EventId = Guid.NewGuid(),
        AnnouncementId = 77,
        SchoolId = 3,
        SchoolName = "Ankara Ortaokulu",
        SenderDisplayName = "Ayşe K.",
        Title = "Veli toplantısı",
        Body = "Cuma 15.00'te spor salonunda.",
        ChunkIndex = 0,
        ChunkCount = 1,
        PublishedAtUtc = DateTime.UtcNow,
        Recipients = [.. recipients]
    };

    private async Task<List<Notification>> AllAsync()
    {
        await using var check = _db.NewContext();
        return await check.Notifications.AsNoTracking().OrderBy(n => n.UserId).ToListAsync();
    }

    [Fact]
    public async Task Writes_one_notification_per_recipient_with_audience_type_and_text()
    {
        var hub = NewHub();
        var e = Evt(
            R(AnnouncementPublishedEvent.StudentAudience, 101, "kc-101"),
            R(AnnouncementPublishedEvent.TeacherAudience, 201, "kc-201"),
            R(AnnouncementPublishedEvent.ParentAudience, 301, "kc-301", studentId: 11, child: "Ali Y."),
            R(AnnouncementPublishedEvent.ParentAudience, 302, "kc-302", studentId: 12));

        await Consumer(hub).Consume(Context(e));

        var rows = await AllAsync();
        rows.Select(n => (n.UserId, n.Type, n.UserKeycloakId)).ShouldBe(
        [
            (101, AnnouncementPublishedConsumer.ToStudentType, "kc-101"),
            (201, AnnouncementPublishedConsumer.ToTeacherType, "kc-201"),
            (301, AnnouncementPublishedConsumer.ToParentType, "kc-301"),
            (302, AnnouncementPublishedConsumer.ToParentType, "kc-302")
        ]);
        rows.Select(n => n.SourceEventId).ShouldBe(e.Recipients.Select(r => (Guid?)r.NotificationId), ignoreOrder: true);
        rows.ShouldAllBe(n => n.Title.Contains("Veli toplantısı") && n.Body.Contains("Cuma 15.00") && n.Body.Contains("Ayşe K.")
                              && n.Body.Contains("Ankara Ortaokulu") && !n.IsRead);
        rows[2].Title.ShouldContain("Ali Y.");
        rows[3].Title.ShouldContain("Çocuğunuz"); // çok çocuklu veli: ad yok
        rows[0].Data!.ShouldContain("\"announcementId\":77");
        rows[0].Data!.ShouldContain("\"studentId\":null");
        rows[2].Data!.ShouldContain("\"studentId\":11");
        hub.Clients.Received(1).User("kc-101");
        hub.Clients.Received(1).User("kc-302");
    }

    [Fact]
    public async Task Redelivery_writes_nothing_new_and_does_not_push_again()
    {
        var e = Evt(R(AnnouncementPublishedEvent.StudentAudience, 101, "kc-101"), R(AnnouncementPublishedEvent.TeacherAudience, 201, "kc-201"));
        await Consumer(NewHub()).Consume(Context(e));
        var hub = NewHub();

        await Consumer(hub).Consume(Context(e));

        (await AllAsync()).Count.ShouldBe(2);
        hub.Clients.DidNotReceive().User(Arg.Any<string>());
    }

    [Fact]
    public async Task Partially_processed_chunk_writes_only_the_missing_recipients()
    {
        var first = R(AnnouncementPublishedEvent.StudentAudience, 101, "kc-101");
        await Consumer(NewHub()).Consume(Context(Evt(first)));
        var e = Evt(first, R(AnnouncementPublishedEvent.StudentAudience, 102, "kc-102"));

        await Consumer(NewHub()).Consume(Context(e));

        (await AllAsync()).Select(n => n.UserId).ShouldBe([101, 102]);
    }

    [Fact]
    public async Task Missing_sub_is_resolved_from_badge_data_and_unresolvable_recipient_is_skipped()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference { UserId = 101, KeycloakId = "kc-pref-101", Locale = "en" });
            ctx.Notifications.Add(new Notification
            {
                UserId = 102, UserKeycloakId = "kc-old-102", Type = "Other", Title = "t", Body = "b", CreatedAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await Consumer(NewHub()).Consume(Context(Evt(
            R(AnnouncementPublishedEvent.StudentAudience, 101),
            R(AnnouncementPublishedEvent.StudentAudience, 102),
            R(AnnouncementPublishedEvent.StudentAudience, 103))));

        var rows = (await AllAsync()).Where(n => n.Type == AnnouncementPublishedConsumer.ToStudentType).ToList();
        rows.Select(n => (n.UserId, n.UserKeycloakId)).ShouldBe([(101, "kc-pref-101"), (102, "kc-old-102")]);
        rows[0].Title.ShouldBe("Announcement: Veli toplantısı"); // alıcının dili
        rows[1].Title.ShouldBe("Duyuru: Veli toplantısı");
    }

    [Fact]
    public async Task Unknown_audience_is_ignored()
    {
        await Consumer(NewHub()).Consume(Context(Evt(R("Admin", 1, "kc-1"), R(AnnouncementPublishedEvent.StudentAudience, 101, "kc-101"))));

        (await AllAsync()).ShouldHaveSingleItem().UserId.ShouldBe(101);
    }
}

using BadgeService.Consumers;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>Issue #1112: haftalık veli raporu — kısa bildirim + çocuğun rapor sekmesi, raporun tamamıyla tek e-posta, kapalı e-posta.</summary>
public class ParentWeeklyReportConsumerTests : IDisposable
{
    private const int ParentUser = 9401;

    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static ConsumeContext<ParentWeeklyReportEvent> Context(ParentWeeklyReportEvent message)
    {
        var ctx = Substitute.For<ConsumeContext<ParentWeeklyReportEvent>>();
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

    private ParentWeeklyReportConsumer Consumer(IEmailSender email) => new(
        _db.NewContext(), NewHub(), FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance, email,
        NullLogger<ParentWeeklyReportConsumer>.Instance);

    private static ParentWeeklyReportEvent Report(string email, string suggestion = "reviewTopic") => new()
    {
        EventId = Guid.NewGuid(),
        StudentId = 21,
        StudentDisplayName = "Ayşe K.",
        WeekStart = new DateTime(2027, 3, 1),
        QuestionsSolved = 40,
        Correct = 30,
        ActiveDays = 4,
        StudyMinutes = 95,
        Points = 120,
        AssignmentsCompleted = 2,
        AssignmentsOverdue = 1,
        Subjects = [new ParentWeeklyReportSubject { Name = "Matematik", Answered = 40, AccuracyPercent = 75 }],
        DroppedTopics = [new ParentWeeklyReportTopicDrop { Name = "Kesirler", AccuracyPercent = 40, PreviousAccuracyPercent = 70 }],
        MockExams = [new ParentWeeklyReportMockExam { Name = "Deneme 1", Net = 31.25m, Rank = 3, Takers = 20 }],
        Badges = ["İlk adım"],
        Suggestion = suggestion,
        SuggestionTopic = "Kesirler",
        RecipientUserId = ParentUser,
        RecipientKeycloakId = $"kc-{ParentUser}",
        RecipientEmail = email,
    };

    [Fact]
    public async Task Parent_gets_one_summary_notification_and_one_full_email_even_on_redelivery()
    {
        var email = new CapturingEmailSender();
        var e = Report("veli@example.com");

        await Consumer(email).Consume(Context(e));
        await Consumer(email).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(ParentWeeklyReportConsumer.Type);
        n.UserId.ShouldBe(ParentUser);
        n.Title.ShouldBe("Ayşe K. için haftalık rapor");
        n.Body.ShouldBe("1 Mart haftası: Ayşe K. 40 soru çözdü, %75 doğru, 4 gün çalıştı. Kesirler konusunu tekrar etmesi iyi olur.");
        n.Data!.ShouldContain("\"studentId\":21");
        n.Data!.ShouldContain("\"weekStart\":\"2027-03-01\"");

        var mail = email.Sent.ShouldHaveSingleItem();
        mail.To.ShouldBe("veli@example.com");
        mail.Subject.ShouldContain("Ayşe K. için haftalık rapor");
        mail.TextBody.ShouldContain("Çalışma süresi: 95 dk · Puan: 120");
        mail.TextBody.ShouldContain("Ödevler: 2 tamamlandı, 1 gecikmiş");
        mail.TextBody.ShouldContain("- Matematik: 40 soru, %75 doğru");
        mail.TextBody.ShouldContain("- Kesirler: %70 → %40");
        mail.TextBody.ShouldContain("- Deneme 1: 31,25 net, 3/20. sıra");
        mail.TextBody.ShouldContain("Kazanılan rozetler: İlk adım");
        mail.HtmlBody.ShouldContain("<br>");
    }

    [Fact]
    public async Task Opted_out_parent_gets_only_the_notification_and_unknown_suggestion_falls_back()
    {
        var email = new CapturingEmailSender();

        await Consumer(email).Consume(Context(Report(string.Empty, suggestion: "bogus")));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).Body.ShouldEndWith("Güzel bir hafta; aynı tempoyla devam.");
        email.Sent.ShouldBeEmpty();
    }
}

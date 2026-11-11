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

/// <summary>Issue #1110: 1 Ağustos sınıf atlaması bildirimi — öğrenci / veli metni, bağlantı verisi, idempotency, bilinmeyen alıcı.</summary>
public class StudentGradePromotedConsumerTests : IDisposable
{
    private const int StudentUser = 9301;
    private const int ParentUser = 9302;

    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private static ConsumeContext<StudentGradePromotedEvent> Context(StudentGradePromotedEvent message)
    {
        var ctx = Substitute.For<ConsumeContext<StudentGradePromotedEvent>>();
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

    private StudentGradePromotedConsumer Consumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<StudentGradePromotedConsumer>.Instance);

    private static StudentGradePromotedEvent Promoted(string audience, int recipient, string sub) => new()
    {
        EventId = Guid.NewGuid(),
        Audience = audience,
        RecipientUserId = recipient,
        RecipientKeycloakId = sub,
        StudentId = 21,
        StudentDisplayName = audience == StudentGradePromotedEvent.ParentAudience ? "Ayşe K." : string.Empty,
        Year = 2027,
        ToGradeLevel = 7,
        PromotedAtUtc = new DateTime(2027, 7, 31, 21, 10, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Student_is_told_the_new_grade_without_a_child_link()
    {
        var hub = NewHub();
        var e = Promoted(StudentGradePromotedEvent.StudentAudience, StudentUser, "kc-student-1110");

        await Consumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(StudentGradePromotedConsumer.ToStudentType);
        n.UserId.ShouldBe(StudentUser);
        n.Title.ShouldBe("Artık 7. sınıftasın");
        n.Data!.ShouldContain("\"studentId\":null");
        n.Data!.ShouldContain("\"toGradeLevel\":7");
        hub.Clients.Received(1).User("kc-student-1110");
    }

    [Fact]
    public async Task Parent_gets_the_child_name_and_link_and_redelivery_is_idempotent()
    {
        var hub = NewHub();
        var e = Promoted(StudentGradePromotedEvent.ParentAudience, ParentUser, "kc-parent-1110");

        await Consumer(hub).Consume(Context(e));
        await Consumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(StudentGradePromotedConsumer.ToParentType);
        n.UserId.ShouldBe(ParentUser);
        n.Body.ShouldContain("Ayşe K. artık 7. sınıfta");
        n.Body.ShouldContain("yenilemede");
        n.Data!.ShouldContain("\"studentId\":21");
        hub.Clients.Received(1).User("kc-parent-1110");
    }

    [Fact]
    public async Task Unknown_audience_writes_nothing()
    {
        var e = Promoted("Teacher", ParentUser, "kc-x");

        await Consumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
    }
}

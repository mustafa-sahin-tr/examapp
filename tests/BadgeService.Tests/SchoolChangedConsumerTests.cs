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
/// issue #542 (#194 K5): okul değişimi bildirimi — alıcı (öğrenci / öğretmen / veli) ve geçişe (değişti / belirlendi / kaldırıldı)
/// göre tip ve metin, öğretmende özel yapılan sınav sayısı, idempotency, sub çözümü.
/// </summary>
public class SchoolChangedConsumerTests : IDisposable
{
    private const int RecipientUser = 9301;
    private const string RecipientSub = "kc-school-9301";

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

    private SchoolChangedConsumer Consumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<SchoolChangedConsumer>.Instance);

    private static SchoolChangedEvent Evt(string audience, int? from = 1, int? to = 2, string sub = RecipientSub,
        int privatized = 0, string child = "") => new()
    {
        EventId = Guid.NewGuid(),
        Audience = audience,
        RecipientUserId = RecipientUser,
        RecipientKeycloakId = sub,
        SubjectUserId = 50,
        StudentId = audience == SchoolChangedEvent.TeacherAudience ? null : 21,
        StudentDisplayName = child,
        FromSchoolId = from,
        FromSchoolName = from is null ? "" : "Ankara Lisesi",
        ToSchoolId = to,
        ToSchoolName = to is null ? "" : "İzmir Lisesi",
        PrivatizedWorksheetCount = privatized,
        ChangedAtUtc = DateTime.UtcNow
    };

    private async Task<Notification> SingleAsync()
    {
        await using var check = _db.NewContext();
        return await check.Notifications.SingleAsync();
    }

    [Fact]
    public async Task Student_school_change_names_both_schools()
    {
        var hub = NewHub();

        await Consumer(hub).Consume(Context(Evt(SchoolChangedEvent.StudentAudience)));

        var n = await SingleAsync();
        n.Type.ShouldBe(SchoolChangedConsumer.ChangedToStudentType);
        n.UserId.ShouldBe(RecipientUser);
        n.UserKeycloakId.ShouldBe(RecipientSub);
        n.Body.ShouldContain("Ankara Lisesi");
        n.Body.ShouldContain("İzmir Lisesi");
        n.Data!.ShouldContain("\"studentId\":null");
        hub.Clients.Received(1).User(RecipientSub);
    }

    [Theory]
    [InlineData(SchoolChangedEvent.StudentAudience, null, 2, SchoolChangedConsumer.AssignedToStudentType)]
    [InlineData(SchoolChangedEvent.StudentAudience, 1, null, SchoolChangedConsumer.RemovedToStudentType)]
    [InlineData(SchoolChangedEvent.TeacherAudience, 1, 2, SchoolChangedConsumer.ChangedToTeacherType)]
    [InlineData(SchoolChangedEvent.TeacherAudience, null, 2, SchoolChangedConsumer.AssignedToTeacherType)]
    [InlineData(SchoolChangedEvent.ParentAudience, 1, 2, SchoolChangedConsumer.ChangedToParentType)]
    [InlineData(SchoolChangedEvent.ParentAudience, null, 2, SchoolChangedConsumer.AssignedToParentType)]
    [InlineData(SchoolChangedEvent.ParentAudience, 1, null, SchoolChangedConsumer.RemovedToParentType)]
    public void Type_follows_audience_and_transition(string audience, int? from, int? to, string expected)
        => SchoolChangedConsumer.TypeFor(Evt(audience, from, to)).ShouldBe(expected);

    [Theory]
    [InlineData(SchoolChangedEvent.TeacherAudience, 1, null)] // öğretmen okulsuz yapılamaz (#541)
    [InlineData(SchoolChangedEvent.StudentAudience, 1, 1)]
    [InlineData(SchoolChangedEvent.StudentAudience, null, null)]
    [InlineData("Admin", 1, 2)]
    public async Task Unknown_audience_or_no_real_change_writes_nothing(string audience, int? from, int? to)
    {
        await Consumer(NewHub()).Consume(Context(Evt(audience, from, to)));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Removed_student_text_names_the_old_school()
    {
        await Consumer(NewHub()).Consume(Context(Evt(SchoolChangedEvent.StudentAudience, to: null)));

        var n = await SingleAsync();
        n.Type.ShouldBe(SchoolChangedConsumer.RemovedToStudentType);
        n.Body.ShouldContain("Ankara Lisesi");
        n.Body.ShouldNotContain("{");
    }

    [Fact]
    public async Task Teacher_message_includes_the_privatized_worksheet_count_only_when_positive()
    {
        await Consumer(NewHub()).Consume(Context(Evt(SchoolChangedEvent.TeacherAudience, privatized: 3)));
        await Consumer(NewHub()).Consume(Context(Evt(SchoolChangedEvent.TeacherAudience, privatized: 0)));

        await using var check = _db.NewContext();
        var bodies = await check.Notifications.OrderBy(n => n.Id).Select(n => n.Body).ToListAsync();
        bodies[0].ShouldContain("3 sınav");
        bodies[1].ShouldNotContain("sınav");
        bodies[1].ShouldEndWith("olarak güncellendi.");
    }

    [Fact]
    public async Task Parent_message_names_the_child_and_links_to_the_child()
    {
        await Consumer(NewHub()).Consume(Context(Evt(SchoolChangedEvent.ParentAudience, child: "Ayşe K.")));

        var n = await SingleAsync();
        n.Type.ShouldBe(SchoolChangedConsumer.ChangedToParentType);
        n.Body.ShouldContain("Ayşe K.");
        n.Data!.ShouldContain("\"studentId\":21");
    }

    [Fact]
    public async Task Missing_names_fall_back_to_default_texts()
    {
        var e = Evt(SchoolChangedEvent.ParentAudience);
        e.FromSchoolName = "";
        await Consumer(NewHub()).Consume(Context(e));

        var n = await SingleAsync();
        n.Body.ShouldContain("Çocuğunuz");
        n.Body.ShouldContain("bir okul");
    }

    [Fact]
    public async Task Redelivered_event_is_idempotent()
    {
        var hub = NewHub();
        var e = Evt(SchoolChangedEvent.StudentAudience);

        await Consumer(hub).Consume(Context(e));
        await Consumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
        hub.Clients.Received(1).User(RecipientSub);
    }

    [Fact]
    public async Task Sub_is_resolved_from_badge_service_data_or_the_message_fails()
    {
        await using (var seed = _db.NewContext())
        {
            seed.UserLocalePreferences.Add(new UserLocalePreference { UserId = RecipientUser, KeycloakId = RecipientSub, UpdatedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        await Consumer(NewHub()).Consume(Context(Evt(SchoolChangedEvent.ParentAudience, sub: "")));
        (await SingleAsync()).UserKeycloakId.ShouldBe(RecipientSub);

        var unknown = Evt(SchoolChangedEvent.ParentAudience, sub: "");
        unknown.RecipientUserId = RecipientUser + 1;
        (await Should.ThrowAsync<Exception>(() => Consumer(NewHub()).Consume(Context(unknown))))
            .ShouldBeOfType<RecipientSubUnresolvedException>();
    }
}

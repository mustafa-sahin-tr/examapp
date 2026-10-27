using BadgeService;
using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

public class ConsumerTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    // ---- AnswerSubmittedConsumer ----

    [Fact]
    public async Task AnswerSubmittedConsumer_aggregates_and_evaluates()
    {
        var hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
        await using var ctx = _db.NewContext();
        var consumer = new AnswerSubmittedConsumer(
            new AnswerSubmissionAggregationService(ctx),
            BadgeService.Tests.Support.BadgeEvaluatorFactory.Create(ctx, hub));

        await consumer.Consume(Context(new AnswerSubmittedEvent
        {
            UserId = 1, IsCorrect = true, QuestionPoint = 10, TimeTakenInSeconds = 20,
            SubjectId = 3, Subject = "Fen", SubmittedAt = DateTime.UtcNow, ClientId = "kc",
        }));

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.SingleAsync(x => x.UserId == 1)).TotalQuestions.ShouldBe(1);
        (await check.StudentDailyActivities.AnyAsync(x => x.UserId == 1)).ShouldBeTrue();
    }

    // ---- QuestionCreatedConsumer ----

    private static IConfiguration Config(bool aiActive) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["QuestionAnalyzer:AIActive"] = aiActive.ToString() })
            .Build();

    private static QuestionCreatedConsumer NewQuestionConsumer(IQuestionClassifier classifier, bool aiActive = true)
        => new(classifier, NullLogger<QuestionCreatedConsumer>.Instance, Config(aiActive));

    // ---- issue #577: book TOC modes ----

    private static QuestionCreatedConsumer NewBookConsumer(IQuestionClassifier classifier, string? mode)
    {
        var values = new Dictionary<string, string?> { ["QuestionAnalyzer:AIActive"] = "true" };
        if (mode is not null)
            values[BookTocModeConfig.Key] = mode;
        return new(classifier, NullLogger<QuestionCreatedConsumer>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    private static QuestionCreatedEvent BookEvent(int? topicId, params int[] subTopicIds) => new()
    {
        QuestionId = 42, ClassificationSource = "Book", GradeId = 5, SubjectId = 1, TopicId = topicId, SubTopicIds = subTopicIds.ToList(),
    };

    private static async Task AssertNoClassificationAsync(IQuestionClassifier classifier)
    {
        await classifier.DidNotReceive().ClassifyAndPersistAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await classifier.DidNotReceive().ClassifyWithinTopicAndPersistAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Hybrid")]
    [InlineData("hybrid")]
    public async Task Book_question_with_a_subtopic_is_not_classified_in_hybrid_mode(string? mode)
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewBookConsumer(classifier, mode).Consume(Context(BookEvent(10, 100)));
        await AssertNoClassificationAsync(classifier);
    }

    [Fact]
    public async Task Book_question_with_a_topic_only_is_classified_within_that_topic_in_hybrid_mode()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewBookConsumer(classifier, "Hybrid").Consume(Context(BookEvent(10)));

        await classifier.Received(1).ClassifyWithinTopicAndPersistAsync(42, 5, 10, Arg.Any<CancellationToken>());
        await classifier.DidNotReceive().ClassifyAndPersistAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Book_question_from_an_older_publisher_without_SubTopicIds_counts_as_no_subtopic()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        var @event = BookEvent(10);
        @event.SubTopicIds = null;
        await NewBookConsumer(classifier, null).Consume(Context(@event));

        await classifier.Received(1).ClassifyWithinTopicAndPersistAsync(42, 5, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Book_question_without_a_topic_is_classified_normally_in_hybrid_mode()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewBookConsumer(classifier, "Hybrid").Consume(Context(BookEvent(null)));

        await classifier.Received(1).ClassifyAndPersistAsync(42, 5, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(null)]
    public async Task Skip_mode_never_classifies_book_questions(int? topicId)
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewBookConsumer(classifier, "Skip").Consume(Context(BookEvent(topicId)));
        await AssertNoClassificationAsync(classifier);
    }

    [Fact]
    public async Task Always_mode_classifies_book_questions_like_human_ones()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewBookConsumer(classifier, "Always").Consume(Context(BookEvent(10, 100)));

        await classifier.Received(1).ClassifyAndPersistAsync(42, 5, Arg.Any<CancellationToken>());
        await classifier.DidNotReceive().ClassifyWithinTopicAndPersistAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Skip")]
    [InlineData("Hybrid")]
    public async Task Book_modes_do_not_change_human_questions(string mode)
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewBookConsumer(classifier, mode).Consume(Context(new QuestionCreatedEvent
        {
            QuestionId = 42, ClassificationSource = "Human", GradeId = 5, TopicId = 10, SubTopicIds = [100],
        }));

        await classifier.Received(1).ClassifyAndPersistAsync(42, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Unknown_BookTocMode_fails_fast()
    {
        Should.Throw<InvalidOperationException>(() => NewBookConsumer(Substitute.For<IQuestionClassifier>(), "Sometimes"))
            .Message.ShouldContain(BookTocModeConfig.Key);
    }

    [Fact]
    public async Task QuestionCreatedConsumer_classifies_a_human_authored_question()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewQuestionConsumer(classifier).Consume(Context(new QuestionCreatedEvent
        {
            QuestionId = 42, ClassificationSource = "Human",
        }));

        await classifier.Received(1).ClassifyAndPersistAsync(42, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuestionCreatedConsumer_passes_the_event_GradeId_to_the_classifier()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewQuestionConsumer(classifier).Consume(Context(new QuestionCreatedEvent
        {
            QuestionId = 42, ClassificationSource = "Human", GradeId = 5,
        }));

        await classifier.Received(1).ClassifyAndPersistAsync(42, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuestionCreatedConsumer_skips_questions_already_classified_by_AI()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewQuestionConsumer(classifier).Consume(Context(new QuestionCreatedEvent
        {
            QuestionId = 42, ClassificationSource = "AI",
        }));

        await classifier.DidNotReceive().ClassifyAndPersistAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuestionCreatedConsumer_does_nothing_when_the_AI_classifier_is_disabled()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewQuestionConsumer(classifier, aiActive: false).Consume(Context(new QuestionCreatedEvent
        {
            QuestionId = 42, ClassificationSource = "Human",
        }));

        await classifier.DidNotReceive().ClassifyAndPersistAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuestionCreatedConsumer_rethrows_classifier_failures_for_masstransit_retry()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        classifier.ClassifyAndPersistAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("gemini down"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            NewQuestionConsumer(classifier).Consume(Context(new QuestionCreatedEvent { QuestionId = 1, ClassificationSource = "Human" })));
    }

    // ---- WorksheetReminderDueConsumer ----

    private static IHubContext<BadgeNotificationHub> NewHub()
    {
        var hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
        hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return hub;
    }

    private WorksheetReminderDueConsumer NewReminderConsumer(IHubContext<BadgeNotificationHub> hub)
        => new(
            _db.NewContext(),
            hub,
            NullLogger<WorksheetReminderDueConsumer>.Instance,
            FallbackUserLocaleResolver.Instance,
            FallbackNotificationTextFactory.Instance);

    private static WorksheetReminderDueEvent ReminderEvent(int reminderId = 500, string keycloakId = "kc-student")
        => new()
        {
            ReminderId = reminderId,
            WorksheetId = 9,
            StudentId = 3,
            UserId = 12,
            UserKeycloakId = keycloakId,
            WorksheetName = "Kesirler Testi",
            ScheduledFor = DateTime.UtcNow.AddHours(1),
            RemindBeforeMinutes = 30,
        };

    [Fact]
    public async Task WorksheetReminderDueConsumer_first_delivery_creates_an_unread_notification()
    {
        var e = ReminderEvent();
        await NewReminderConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe("WorksheetReminderDue");
        n.SourceReminderId.ShouldBe(e.ReminderId);
        n.UserId.ShouldBe(e.UserId);
        n.IsRead.ShouldBeFalse();
    }

    [Fact]
    public async Task WorksheetReminderDueConsumer_duplicate_event_does_not_create_a_second_notification()
    {
        var e = ReminderEvent();

        await NewReminderConsumer(NewHub()).Consume(Context(e));
        await NewReminderConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.SourceReminderId == e.ReminderId)).ShouldBe(1);
    }

    [Fact]
    public async Task WorksheetReminderDueConsumer_pushes_to_the_student_via_signalr()
    {
        var hub = NewHub();
        var e = ReminderEvent(keycloakId: "kc-abc");

        await NewReminderConsumer(hub).Consume(Context(e));

        await hub.Clients.User("kc-abc").Received(1).SendCoreAsync(
            "ReminderDue", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WorksheetReminderDueConsumer_missing_keycloak_id_skips_push_but_still_saves_notification()
    {
        var hub = NewHub();
        var e = ReminderEvent(keycloakId: "");

        await NewReminderConsumer(hub).Consume(Context(e));

        await hub.Clients.User(Arg.Any<string>()).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());

        await using var check = _db.NewContext();
        (await check.Notifications.AnyAsync(n => n.SourceReminderId == e.ReminderId)).ShouldBeTrue();
    }

    public void Dispose() => _db.Dispose();
}

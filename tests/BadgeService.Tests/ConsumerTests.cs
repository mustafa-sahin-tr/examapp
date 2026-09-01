using BadgeService;
using BadgeService.Consumers;
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
            new BadgeEvaluator(ctx, hub));

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

    [Fact]
    public async Task QuestionCreatedConsumer_classifies_a_human_authored_question()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewQuestionConsumer(classifier).Consume(Context(new QuestionCreatedEvent
        {
            QuestionId = 42, ClassificationSource = "Human",
        }));

        await classifier.Received(1).ClassifyAndPersistAsync(42, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuestionCreatedConsumer_skips_questions_already_classified_by_AI()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewQuestionConsumer(classifier).Consume(Context(new QuestionCreatedEvent
        {
            QuestionId = 42, ClassificationSource = "AI",
        }));

        await classifier.DidNotReceive().ClassifyAndPersistAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuestionCreatedConsumer_does_nothing_when_the_AI_classifier_is_disabled()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        await NewQuestionConsumer(classifier, aiActive: false).Consume(Context(new QuestionCreatedEvent
        {
            QuestionId = 42, ClassificationSource = "Human",
        }));

        await classifier.DidNotReceive().ClassifyAndPersistAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuestionCreatedConsumer_rethrows_classifier_failures_for_masstransit_retry()
    {
        var classifier = Substitute.For<IQuestionClassifier>();
        classifier.ClassifyAndPersistAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("gemini down"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            NewQuestionConsumer(classifier).Consume(Context(new QuestionCreatedEvent { QuestionId = 1, ClassificationSource = "Human" })));
    }

    public void Dispose() => _db.Dispose();
}

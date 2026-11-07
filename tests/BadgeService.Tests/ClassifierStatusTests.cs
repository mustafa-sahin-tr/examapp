using BadgeService.Consumers;
using BadgeService.Controllers;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

// Issue #897: yönetici "Sınıflandırıcı" kartı — etkin sağlayıcı, model, anahtar var mı ve son 24 saatin sayıları.
public class ClassifierStatusTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    private static ClassifierStatusDto Status(IConfiguration config, ClassifierActivity activity) =>
        (ClassifierStatusDto)((OkObjectResult)new ClassifierStatusController(config, activity).Get().Result!).Value!;

    [Fact]
    public void Activity_counts_the_last_24_hours_and_keeps_only_the_error_type()
    {
        var clock = new Clock(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        var activity = new ClassifierActivity(clock);

        activity.Record(ClassifierActivity.Outcome.Classified);
        clock.Now = clock.Now.AddHours(20);
        activity.Record(ClassifierActivity.Outcome.Classified);
        activity.Record(ClassifierActivity.Outcome.Skipped);
        activity.Record(ClassifierActivity.Outcome.Failed, new HttpRequestException("secret provider body"));
        clock.Now = clock.Now.AddHours(5);

        var snapshot = activity.Snapshot();
        (snapshot.Classified, snapshot.Skipped, snapshot.Failed).ShouldBe((1, 1, 1));
        snapshot.LastErrorKind.ShouldBe(nameof(HttpRequestException));
        snapshot.LastErrorAt.ShouldBe(new DateTime(2026, 11, 8, 6, 0, 0, DateTimeKind.Utc));
        snapshot.StartedAt.ShouldBe(new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Consumer_records_classified_skipped_and_failed()
    {
        var activity = new ClassifierActivity(TimeProvider.System);
        var classifier = Substitute.For<IQuestionClassifier>();
        var consumer = new QuestionCreatedConsumer(classifier, NullLogger<QuestionCreatedConsumer>.Instance,
            Config(("QuestionAnalyzer:AIActive", "true")), activity);

        await consumer.Consume(Context(new QuestionCreatedEvent { QuestionId = 1, ClassificationSource = "Human" }));
        await consumer.Consume(Context(new QuestionCreatedEvent { QuestionId = 2, ClassificationSource = "AI" }));
        classifier.ClassifyAndPersistAsync(3, Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new TimeoutException()));
        await Should.ThrowAsync<TimeoutException>(() => consumer.Consume(Context(new QuestionCreatedEvent { QuestionId = 3, ClassificationSource = "Human" })));

        var snapshot = activity.Snapshot();
        (snapshot.Classified, snapshot.Skipped, snapshot.Failed).ShouldBe((1, 1, 1));
        snapshot.LastErrorKind.ShouldBe(nameof(TimeoutException));
    }

    [Fact]
    public void Status_reports_claude_by_default_without_leaking_the_key()
    {
        var status = Status(
            Config(("Anthropic:ApiKey", "sk-ant-secret"), ("Anthropic:Model", "claude-haiku-5-5"), ("QuestionAnalyzer:BookTocMode", "Skip")),
            new ClassifierActivity(TimeProvider.System));

        status.Provider.ShouldBe("Claude");
        status.Model.ShouldBe("claude-haiku-5-5");
        status.Active.ShouldBeTrue();
        status.ApiKeyConfigured.ShouldBeTrue();
        status.BookTocMode.ShouldBe("Skip");
        System.Text.Json.JsonSerializer.Serialize(status).ShouldNotContain("sk-ant");
    }

    [Fact]
    public void Status_reports_gemini_with_its_own_key_and_a_disabled_classifier()
    {
        var status = Status(
            Config(("QuestionAnalyzer:Provider", "gemini"), ("QuestionAnalyzer:AIActive", "false"), ("Anthropic:ApiKey", "sk-ant-secret")),
            new ClassifierActivity(TimeProvider.System));

        status.Provider.ShouldBe("Gemini");
        status.Model.ShouldBe(new GeminiOptions().Model);
        status.Active.ShouldBeFalse();
        status.ApiKeyConfigured.ShouldBeFalse();
        status.BookTocMode.ShouldBe("Hybrid");
    }
}

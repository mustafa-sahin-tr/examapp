using System.Net;
using BadgeService.Consumers;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #84: <see cref="LoginAttemptedConsumer"/> bridges auth-api's outbox event to
/// exam-api's <c>POST /api/login-events</c>, with idempotency keyed on the producer-assigned
/// <see cref="LoginAttemptedEvent.EventId"/> (not on the user/timestamp/success tuple — two
/// distinct attempts can share a UTC tick under load).
/// </summary>
public class LoginAttemptedConsumerTests : IDisposable
{
    private const string ExamApi = "https://exam.test";
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    private static ConsumeContext<LoginAttemptedEvent> Context(LoginAttemptedEvent message)
    {
        var ctx = Substitute.For<ConsumeContext<LoginAttemptedEvent>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ExamApi:BaseUrl"] = ExamApi })
            .Build();

    private LoginAttemptedConsumer NewConsumer(StubHttp http) => new(
        _db.NewContext(),
        http,
        StubTokenProvider(),
        Config(),
        NullLogger<LoginAttemptedConsumer>.Instance);

    private static IServiceTokenProvider StubTokenProvider()
    {
        var provider = Substitute.For<IServiceTokenProvider>();
        provider.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("test-token");
        return provider;
    }

    private static LoginAttemptedEvent Event(bool success = true, string keycloakUserId = "kc-user-1") => new()
    {
        KeycloakUserId = keycloakUserId,
        Role = "Student",
        OccurredAtUtc = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc),
        Success = success,
    };

    [Fact]
    public async Task Consume_posts_the_event_to_exam_api_login_events_endpoint()
    {
        var http = new StubHttp().On("/api/login-events", HttpStatusCode.Created, "{}");
        var e = Event();

        await NewConsumer(http).Consume(Context(e));

        var request = http.Requests.Single();
        request.RequestUri!.ToString().ShouldBe($"{ExamApi}/api/login-events");
        request.Method.ShouldBe(HttpMethod.Post);

        var body = http.BodyMatching("/api/login-events");
        body.ShouldContain("\"keycloakUserId\":\"kc-user-1\"");
        body.ShouldContain("\"role\":\"Student\"");
        body.ShouldContain("\"success\":true");
    }

    [Fact]
    public async Task Consume_posts_failed_login_attempts_too()
    {
        var http = new StubHttp().On("/api/login-events", HttpStatusCode.Created, "{}");
        var e = Event(success: false);

        await NewConsumer(http).Consume(Context(e));

        var body = http.BodyMatching("/api/login-events");
        body.ShouldContain("\"success\":false");
    }

    [Fact]
    public async Task Consume_records_the_processed_attempt_after_a_successful_post()
    {
        var http = new StubHttp().On("/api/login-events", HttpStatusCode.Created, "{}");
        var e = Event();

        await NewConsumer(http).Consume(Context(e));

        await using var check = _db.NewContext();
        var recorded = check.ProcessedLoginAttempts.Single();
        recorded.EventId.ShouldBe(e.EventId);
        recorded.KeycloakUserId.ShouldBe(e.KeycloakUserId);
        recorded.OccurredAtUtc.ShouldBe(e.OccurredAtUtc);
        recorded.Success.ShouldBe(e.Success);
    }

    [Fact]
    public async Task Consume_is_idempotent_a_redelivered_message_does_not_post_again()
    {
        var http = new StubHttp().On("/api/login-events", HttpStatusCode.Created, "{}");
        var e = Event();

        await NewConsumer(http).Consume(Context(e));
        await NewConsumer(http).Consume(Context(e)); // simulated redelivery, same tuple

        http.Requests.Count.ShouldBe(1);

        await using var check = _db.NewContext();
        check.ProcessedLoginAttempts.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Consume_a_different_login_attempt_still_posts_independently_of_a_previous_one()
    {
        var http = new StubHttp().On("/api/login-events", HttpStatusCode.Created, "{}");

        await NewConsumer(http).Consume(Context(Event(keycloakUserId: "kc-user-1")));
        await NewConsumer(http).Consume(Context(Event(keycloakUserId: "kc-user-2")));

        http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Consume_two_distinct_attempts_sharing_the_same_tick_both_post()
    {
        // Regression guard: dedup used to be keyed on (KeycloakUserId, OccurredAtUtc, Success),
        // so two genuinely different attempts landing in the same UTC tick would be wrongly
        // collapsed into one. EventId-based dedup must treat them as independent.
        var http = new StubHttp().On("/api/login-events", HttpStatusCode.Created, "{}");
        var sameTick = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
        var first = Event();
        first.OccurredAtUtc = sameTick;
        var second = Event();
        second.OccurredAtUtc = sameTick;

        await NewConsumer(http).Consume(Context(first));
        await NewConsumer(http).Consume(Context(second));

        http.Requests.Count.ShouldBe(2);

        await using var check = _db.NewContext();
        check.ProcessedLoginAttempts.Count().ShouldBe(2);
    }

    [Fact]
    public async Task Consume_rethrows_when_exam_api_responds_with_a_server_error()
    {
        var http = new StubHttp().On("/api/login-events", HttpStatusCode.InternalServerError, "boom");
        var e = Event();

        await Should.ThrowAsync<InvalidOperationException>(() => NewConsumer(http).Consume(Context(e)));

        await using var check = _db.NewContext();
        check.ProcessedLoginAttempts.Any().ShouldBeFalse(); // failure isn't recorded as processed
    }

    public void Dispose() => _db.Dispose();
}

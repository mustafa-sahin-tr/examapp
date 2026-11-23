using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssistantService.Consumers;
using AssistantService.Data;
using AssistantService.Endpoints;
using AssistantService.Retention;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantService.Tests;

/// <summary>Issue #505: conversation history endpoints, the retention purge and the student reset consumer.</summary>
public sealed class HistoryRetentionTests : IAsyncDisposable
{
    private readonly ToolHarness _h = new();
    private readonly AssistantOptions _options = new() { RetentionDays = 30 };
    private WebApplication? _app;

    private DateTime Now => _h.Clock.Now.UtcDateTime;

    private AssistantDataCleaner Cleaner(AssistantDbContext db) =>
        new(db, new StaticOptions(_options), _h.Clock);

    private async Task<Guid> SeedAsync(string sub, DateTime createdAt, DateTime? updatedAt = null, params (string Role, string Text, DateTime At)[] messages)
    {
        await using var db = _h.NewDb();
        var c = new Conversation
        {
            Id = Guid.NewGuid(), UserSub = sub, Role = "Student", Title = "t-" + sub,
            CreatedAt = createdAt, UpdatedAt = updatedAt ?? createdAt,
        };
        db.Conversations.Add(c);
        foreach (var (role, text, at) in messages)
        {
            AIContent content = role == "tool"
                ? new FunctionResultContent("call-1", new { secret = "tool output" })
                : new TextContent(text);
            db.Messages.Add(new Message
            {
                ConversationId = c.Id, Role = role, CreatedAt = at,
                ContentJson = JsonSerializer.Serialize(new List<AIContent> { content }, AIJsonUtilities.DefaultOptions),
            });
        }
        db.PendingActions.Add(new PendingAction
        {
            Id = Guid.NewGuid(), ConversationId = c.Id, UserSub = sub, Tool = "start_daily_practice",
            CreatedAt = createdAt, ExpiresAt = createdAt.AddMinutes(10),
        });
        await db.SaveChangesAsync();
        return c.Id;
    }

    // ---- endpoints -------------------------------------------------------------------------------------------------

    private async Task<HttpClient> ClientAsync(string sub)
    {
        if (_app is null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddScoped(_ => _h.NewDb());
            builder.Services.AddAuthentication(SubAuth.Scheme).AddScheme<AuthenticationSchemeOptions, SubAuth>(SubAuth.Scheme, _ => { });
            builder.Services.AddAuthorization();
            _app = builder.Build();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.MapConversationEndpoints();
            await _app.StartAsync();
        }
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(SubAuth.Header, sub);
        return client;
    }

    [Fact]
    public async Task List_returns_only_the_callers_conversations_newest_first()
    {
        var older = await SeedAsync("student-1", Now.AddHours(-2));
        var newer = await SeedAsync("student-1", Now.AddHours(-1));
        await SeedAsync("student-2", Now);

        var body = await (await ClientAsync("student-1")).GetFromJsonAsync<JsonObject>("/api/assistant/conversations");

        var ids = body!["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ToList();
        ids.ShouldBe([newer, older]);
    }

    [Fact]
    public async Task Get_returns_user_and_assistant_text_but_not_tool_results()
    {
        var id = await SeedAsync("student-1", Now, null,
            ("user", "Zorlandığım konular?", Now), ("tool", "", Now), ("assistant", "Açılar konusunda zorlanıyorsun.", Now));

        var body = await (await ClientAsync("student-1")).GetFromJsonAsync<JsonObject>($"/api/assistant/conversations/{id}");

        var messages = body!["messages"]!.AsArray();
        messages.Select(m => m!["role"]!.GetValue<string>()).ShouldBe(["user", "assistant"]);
        messages[1]!["text"]!.GetValue<string>().ShouldBe("Açılar konusunda zorlanıyorsun.");
        body.ToJsonString().ShouldNotContain("tool output");
    }

    [Fact]
    public async Task Someone_elses_conversation_is_404_for_read_and_delete()
    {
        var id = await SeedAsync("student-2", Now, null, ("user", "gizli", Now));
        var client = await ClientAsync("student-1");

        (await client.GetAsync($"/api/assistant/conversations/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/assistant/conversations/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var db = _h.NewDb();
        (await db.Conversations.AnyAsync(c => c.Id == id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_removes_the_conversation_with_its_messages_and_pending_actions()
    {
        var id = await SeedAsync("student-1", Now, null, ("user", "selam", Now), ("assistant", "merhaba", Now));
        var client = await ClientAsync("student-1");

        (await client.DeleteAsync($"/api/assistant/conversations/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var db = _h.NewDb();
        (await db.Conversations.AnyAsync()).ShouldBeFalse();
        (await db.Messages.AnyAsync()).ShouldBeFalse();
        (await db.PendingActions.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_all_clears_only_the_callers_history()
    {
        await SeedAsync("student-1", Now, null, ("user", "a", Now));
        await SeedAsync("student-1", Now, null, ("user", "b", Now));
        var other = await SeedAsync("student-2", Now, null, ("user", "c", Now));

        var response = await (await ClientAsync("student-1")).DeleteAsync("/api/assistant/conversations");

        (await response.Content.ReadFromJsonAsync<JsonObject>())!["deleted"]!.GetValue<int>().ShouldBe(2);
        await using var db = _h.NewDb();
        (await db.Conversations.Select(c => c.Id).ToListAsync()).ShouldBe([other]);
    }

    // ---- retention purge -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Purge_deletes_conversations_idle_past_the_retention_window_and_keeps_recent_ones()
    {
        var expired = await SeedAsync("student-1", Now.AddDays(-40), Now.AddDays(-31), ("user", "eski", Now.AddDays(-31)));
        var recent = await SeedAsync("student-1", Now.AddDays(-2), null, ("user", "yeni", Now.AddDays(-2)));
        await using (var seed = _h.NewDb())
        {
            seed.Usage.Add(new UsageDay { UserSub = "student-1", Day = DateOnly.FromDateTime(Now.AddDays(-31)), Messages = 3 });
            seed.Usage.Add(new UsageDay { UserSub = "student-1", Day = DateOnly.FromDateTime(Now), Messages = 1 });
            await seed.SaveChangesAsync();
        }

        await using var db = _h.NewDb();
        var result = await Cleaner(db).PurgeExpiredAsync(CancellationToken.None);

        result.Conversations.ShouldBe(1);
        (await db.Conversations.Select(c => c.Id).ToListAsync()).ShouldBe([recent]);
        (await db.Messages.CountAsync()).ShouldBe(1);
        (await db.PendingActions.AnyAsync(p => p.ConversationId == expired)).ShouldBeFalse();
        (await db.Usage.Select(u => u.Day).ToListAsync()).ShouldBe([DateOnly.FromDateTime(Now)]);
    }

    [Fact]
    public async Task Purge_drops_old_messages_inside_a_conversation_that_is_still_in_use()
    {
        var id = await SeedAsync("student-1", Now.AddDays(-45), Now.AddDays(-1),
            ("user", "çok eski", Now.AddDays(-45)), ("user", "dün", Now.AddDays(-1)));

        await using var db = _h.NewDb();
        await Cleaner(db).PurgeExpiredAsync(CancellationToken.None);

        (await db.Conversations.AnyAsync(c => c.Id == id)).ShouldBeTrue();
        (await db.Messages.CountAsync()).ShouldBe(1);
        // The conversation's own pending action was created 45 days ago: gone as well.
        (await db.PendingActions.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Purge_follows_the_configured_retention()
    {
        _options.RetentionDays = 7;
        await SeedAsync("student-1", Now.AddDays(-8));

        await using var db = _h.NewDb();
        (await Cleaner(db).PurgeExpiredAsync(CancellationToken.None)).Conversations.ShouldBe(1);
    }

    [Fact]
    public async Task The_background_pass_logs_failures_instead_of_stopping()
    {
        var services = new ServiceCollection();
        services.AddScoped<AssistantDataCleaner>(_ => throw new InvalidOperationException("db down"));
        await using var provider = services.BuildServiceProvider();
        var service = new RetentionPurgeService(provider.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptions(_options), _h.Clock, NullLogger<RetentionPurgeService>.Instance);

        await Should.NotThrowAsync(() => service.RunOnceAsync(CancellationToken.None));
    }

    // ---- student reset consumer ------------------------------------------------------------------------------------

    private async Task ConsumeAsync(StudentProgressResetEvent evt)
    {
        await using var db = _h.NewDb();
        var consumer = new StudentProgressResetConsumer(Cleaner(db), NullLogger<StudentProgressResetConsumer>.Instance);
        var context = Substitute.For<ConsumeContext<StudentProgressResetEvent>>();
        context.Message.Returns(evt);
        context.CancellationToken.Returns(CancellationToken.None);
        await consumer.Consume(context);
    }

    [Fact]
    public async Task A_student_reset_deletes_that_students_conversations_from_before_the_reset_only()
    {
        var resetAt = Now;
        await SeedAsync("student-1", resetAt.AddDays(-3), null, ("user", "eski", resetAt.AddDays(-3)));
        var afterReset = await SeedAsync("student-1", resetAt.AddMinutes(1));
        var other = await SeedAsync("student-2", resetAt.AddDays(-3));

        var evt = new StudentProgressResetEvent { EventId = Guid.NewGuid(), KeycloakId = "student-1", UserId = 7, StudentId = 70, ResetAtUtc = resetAt };
        await ConsumeAsync(evt);
        await ConsumeAsync(evt); // redelivery: nothing more is deleted

        await using var db = _h.NewDb();
        (await db.Conversations.Select(c => c.Id).OrderBy(c => c).ToListAsync()).ShouldBe(new[] { afterReset, other }.Order().ToList());
        (await db.Messages.AnyAsync()).ShouldBeFalse();
        (await db.PendingActions.CountAsync(p => p.UserSub == "student-1")).ShouldBe(1); // the post-reset conversation's
    }

    [Fact]
    public async Task A_reset_without_a_keycloak_id_is_acknowledged_and_deletes_nothing()
    {
        await SeedAsync("student-1", Now.AddDays(-1));

        await ConsumeAsync(new StudentProgressResetEvent { EventId = Guid.NewGuid(), KeycloakId = " ", ResetAtUtc = Now });

        await using var db = _h.NewDb();
        (await db.Conversations.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task The_consumer_retries_then_dead_letters_on_unexpected_errors()
    {
        var consumer = new StudentProgressResetConsumer(Cleaner(_h.NewDb()), NullLogger<StudentProgressResetConsumer>.Instance);
        var context = Substitute.For<ConsumeContext<StudentProgressResetEvent>>();
        context.Message.Returns(new StudentProgressResetEvent { KeycloakId = "student-1", ResetAtUtc = Now });
        context.CancellationToken.Returns(new CancellationToken(canceled: true));

        await Should.ThrowAsync<OperationCanceledException>(() => consumer.Consume(context));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        _h.Dispose();
    }

    private sealed class StaticOptions(AssistantOptions value) : IOptionsMonitor<AssistantOptions>
    {
        public AssistantOptions CurrentValue => value;
        public AssistantOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AssistantOptions, string?> listener) => null;
    }

    private sealed class SubAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Test";
        public const string Header = "X-Test-Sub";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Request.Headers.TryGetValue(Header, out var sub)
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", sub!), new Claim(ClaimTypes.Role, "Student")], Scheme)), Scheme))
                : AuthenticateResult.NoResult());
    }
}

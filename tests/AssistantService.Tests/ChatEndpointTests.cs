using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using AssistantService.Chat;
using AssistantService.Data;
using AssistantService.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using static AssistantService.Tests.FakeChatClient;

namespace AssistantService.Tests;

/// <summary>Issue #504: HTTP behaviour of /chat, /usage/me and feedback.</summary>
public sealed class ChatEndpointTests : IAsyncDisposable
{
    private readonly ChatHarness _h = new();
    private WebApplication? _app;

    private async Task<HttpClient> ClientAsync(string sub = "student-1")
    {
        if (_app is null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddScoped(_ => _h.Tools.NewDb());
            builder.Services.AddScoped(sp => _h.Runner(sp.GetRequiredService<AssistantDbContext>()));
            builder.Services.AddSingleton(_h.Limiter);
            builder.Services.AddSingleton<TimeProvider>(_h.Tools.Clock);
            builder.Services.AddAuthentication(SubAuth.Scheme).AddScheme<AuthenticationSchemeOptions, SubAuth>(SubAuth.Scheme, _ => { });
            builder.Services.AddAuthorization();
            _app = builder.Build();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.MapChatEndpoints();
            await _app.StartAsync();
        }
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(SubAuth.Header, sub);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "token-" + sub);
        return client;
    }

    private static Task<HttpResponseMessage> ChatAsync(HttpClient client, object body) => client.PostAsJsonAsync("/api/assistant/chat", body);

    [Fact]
    public async Task Chat_streams_server_sent_events()
    {
        _h.Model.Then(Text("Merhaba"), Usage(1, 1));
        var client = await ClientAsync();

        var response = await ChatAsync(client, new { message = "selam" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("event: text\ndata: {\"delta\":\"Merhaba\"}\n\n");
        body.ShouldContain("event: done\n");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_messages_are_rejected(string message)
    {
        var response = await ChatAsync(await ClientAsync(), new { message });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Too_long_messages_are_rejected()
    {
        var response = await ChatAsync(await ClientAsync(), new { message = new string('a', ChatTurnRunner.MaxMessageLength + 1) });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_request_from_a_test_screen_is_refused()
    {
        var response = await ChatAsync(await ClientAsync(), new { message = "cevap ne", context = new { route = "/testsolve/5" } });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>().ShouldBe("AssistantClosedDuringTest");
        _h.Model.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_users_conversation_is_404()
    {
        _h.Model.Then(Text("x"), Usage(1, 1));
        await ChatAsync(await ClientAsync(), new { message = "selam" });
        await using var db = _h.Tools.NewDb();
        var id = (await db.Conversations.SingleAsync()).Id;

        var other = await ClientAsync("student-2");
        (await ChatAsync(other, new { conversationId = id, message = "selam" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Over_the_hourly_limit_is_429_with_retry_after()
    {
        _h.Options.MessagesPerHour = 1;
        _h.Model.Then(Text("x"), Usage(1, 1));
        var client = await ClientAsync();
        await ChatAsync(client, new { message = "bir" });

        var response = await ChatAsync(client, new { message = "iki" });

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.ShouldNotBeNull();
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        body["code"]!.GetValue<string>().ShouldBe("AssistantBudgetExceeded");
        body["limit"]!.GetValue<string>().ShouldBe("MessagesPerHour");
        var usage = (await client.GetFromJsonAsync<JsonObject>("/api/assistant/usage/me"))!;
        usage["messagesThisHour"]!.GetValue<long>().ShouldBe(1);
    }

    [Fact]
    public async Task A_model_error_ends_with_an_error_event_and_leaves_no_empty_conversation()
    {
        var response = await ChatAsync(await ClientAsync(), new { message = "selam" }); // no scripted reply → model throws

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"code\":\"AssistantUnavailable\"");
        await using var db = _h.Tools.NewDb();
        (await db.Conversations.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Feedback_on_own_assistant_messages_only_and_a_new_vote_replaces_the_old()
    {
        _h.Model.Then(Text("x"), Usage(1, 1));
        var client = await ClientAsync();
        await ChatAsync(client, new { message = "selam" });
        long assistantId, userId;
        await using (var db = _h.Tools.NewDb())
        {
            assistantId = (await db.Messages.SingleAsync(m => m.Role == "assistant")).Id;
            userId = (await db.Messages.SingleAsync(m => m.Role == "user")).Id;
        }

        (await client.PostAsJsonAsync($"/api/assistant/messages/{assistantId}/feedback", new { rating = "down", reason = "wrongInfo" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync($"/api/assistant/messages/{assistantId}/feedback", new { rating = "up", reason = "wrongInfo" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync($"/api/assistant/messages/{userId}/feedback", new { rating = "up" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync($"/api/assistant/messages/{assistantId}/feedback", new { rating = "meh" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/assistant/messages/{assistantId}/feedback", new { rating = "down", reason = "<x>" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var other = await ClientAsync("student-2");
        (await other.PostAsJsonAsync($"/api/assistant/messages/{assistantId}/feedback", new { rating = "down" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var check = _h.Tools.NewDb();
        var feedback = await check.Feedback.SingleAsync();
        feedback.Rating.ShouldBe(1);
        feedback.Reason.ShouldBeNull();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        _h.Dispose();
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

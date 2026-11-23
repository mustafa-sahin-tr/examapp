using System.Text.Json;
using AssistantService.Chat;
using AssistantService.Data;
using Microsoft.EntityFrameworkCore;

namespace AssistantService.Endpoints;

/// <summary>Issue #504: chat (SSE), the user's usage and message feedback.</summary>
public static class ChatEndpoints
{
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    public sealed record ChatBody(Guid? ConversationId, string? Message, ChatContext? Context);

    public sealed record FeedbackBody(string? Rating, string? Reason);

    public static readonly string[] FeedbackReasons = ["wrongInfo", "notHelpful", "tooLong", "inappropriate", "other"];

    public static WebApplication MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(AssistantGate.PathPrefix).RequireAuthorization();

        group.MapPost("/chat", async (ChatBody body, HttpContext http, ChatTurnRunner runner, UsageLimiter limiter,
            ILoggerFactory loggers, IHostEnvironment env) =>
        {
            var context = ToolEndpoints.ContextFrom(http);
            var text = body.Message?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > ChatTurnRunner.MaxMessageLength)
                return Results.BadRequest(new { code = "InvalidMessage", maxLength = ChatTurnRunner.MaxMessageLength });
            // The panel is hidden during a test; a request that still comes from one is refused here too.
            if (ChatTurnRunner.IsClosedRoute(body.Context?.Route))
                return Results.Json(new { code = "AssistantClosedDuringTest" }, statusCode: StatusCodes.Status409Conflict);

            var conversation = await runner.LoadOrCreateAsync(context, body.ConversationId, http.RequestAborted);
            if (conversation is null)
                return Results.NotFound(new { code = "ConversationNotFound" });

            var (exceeded, retryAfter) = await limiter.TryStartTurnAsync(context.UserSub);
            if (exceeded != LimitExceeded.None)
            {
                if (retryAfter is { } wait)
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
                return Results.Json(new { code = "AssistantBudgetExceeded", limit = exceeded.ToString() },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            // Headers go out now; the model call runs inside the stream (#502 spike: the gateway timeout only covers headers).
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            await http.Response.Body.FlushAsync(http.RequestAborted);

            await using var sink = new SseSink(http.Response, KeepAliveInterval, http.RequestAborted);
            try
            {
                await runner.RunAsync(context, conversation, new ChatRequest(body.ConversationId, text, body.Context), sink);
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
            {
                // client went away
            }
            catch (Exception ex)
            {
                loggers.CreateLogger("AssistantChat").LogError(ex, "[Assistant] chat turn failed");
                // Development only: the cause, for local debugging (never in production: may carry provider details).
                await sink.SendAsync("error", env.IsDevelopment()
                    ? new { code = "AssistantUnavailable", detail = $"{ex.GetType().Name}: {ex.Message}" }
                    : (object)new { code = "AssistantUnavailable" });
            }
            return Results.Empty;
        });

        group.MapGet("/usage/me", async (HttpContext http, UsageLimiter limiter) =>
        {
            var s = await limiter.SnapshotAsync(ToolEndpoints.ContextFrom(http).UserSub);
            return Results.Ok(new
            {
                messagesThisHour = s.MessagesThisHour,
                messagesPerHour = s.MessagesPerHour,
                spentTodayUsd = s.SpentTodayUsd,
                dailyBudgetUsd = s.DailyBudgetUsd,
                totalBudgetReached = s.TotalBudgetReached,
            });
        });

        // 👍 / 👎 on one of the user's own assistant messages; one per message, a new vote replaces it.
        group.MapPost("/messages/{id:long}/feedback", async (long id, FeedbackBody body, HttpContext http, AssistantDbContext db,
            TimeProvider clock) =>
        {
            var sub = ToolEndpoints.ContextFrom(http).UserSub;
            var rating = body.Rating switch { "up" => 1, "down" => -1, _ => 0 };
            if (rating == 0)
                return Results.BadRequest(new { code = "InvalidRating" });
            if (body.Reason is not null && !FeedbackReasons.Contains(body.Reason))
                return Results.BadRequest(new { code = "InvalidReason", reasons = FeedbackReasons });

            var owned = await db.Messages.AnyAsync(m => m.Id == id && m.Role == "assistant" && m.Conversation.UserSub == sub, http.RequestAborted);
            if (!owned)
                return Results.NotFound(new { code = "MessageNotFound" });

            var feedback = await db.Feedback.AsTracking().FirstOrDefaultAsync(f => f.MessageId == id && f.UserSub == sub, http.RequestAborted);
            if (feedback is null)
            {
                feedback = new Feedback { MessageId = id, UserSub = sub };
                db.Feedback.Add(feedback);
            }
            feedback.Rating = rating;
            feedback.Reason = rating < 0 ? body.Reason : null;
            feedback.CreatedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(http.RequestAborted);
            return Results.NoContent();
        });

        return app;
    }
}

/// <summary>Server-sent events with a periodic <c>: keep-alive</c> comment (Cloudflare Tunnel cuts ~100 s idle).</summary>
public sealed class SseSink : IChatEventSink, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpResponse _response;
    private readonly CancellationToken _ct;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly PeriodicTimer _timer;
    private readonly Task _keepAlive;

    public SseSink(HttpResponse response, TimeSpan keepAlive, CancellationToken ct)
    {
        _response = response;
        _ct = ct;
        _timer = new PeriodicTimer(keepAlive);
        _keepAlive = KeepAliveAsync();
    }

    public Task SendAsync(string type, object payload) =>
        WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(payload, Json)}\n\n");

    private async Task KeepAliveAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_ct))
                await WriteAsync(": keep-alive\n\n");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WriteAsync(string text)
    {
        await _lock.WaitAsync(_ct);
        try
        {
            await _response.WriteAsync(text, _ct);
            await _response.Body.FlushAsync(_ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _timer.Dispose();
        await _keepAlive;
        _lock.Dispose();
    }
}

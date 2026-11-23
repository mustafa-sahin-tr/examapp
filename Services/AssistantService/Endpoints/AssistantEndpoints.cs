using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace AssistantService.Endpoints;

public static class AssistantEndpoints
{
    public static WebApplication MapAssistantEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(AssistantGate.PathPrefix).RequireAuthorization();

        // The panel (#507) asks this before showing itself; the gate already answered 401 / 503 / 403.
        group.MapGet("/status", (ClaimsPrincipal user, IOptionsMonitor<AssistantOptions> options) =>
        {
            var o = options.CurrentValue;
            return Results.Ok(new
            {
                enabled = true,
                roles = o.Roles.Where(user.IsInRole).ToArray(),
                limits = new { o.MessagesPerHour, o.ImageExplanationsPerDay },
            });
        });

        // #502 spike: is an SSE stream passed through the gateway (Ocelot) without buffering, and does a long
        // stream survive the route timeout? Development only.
        if (app.Environment.IsDevelopment())
        {
            group.MapGet("/spike/stream", async (HttpContext context, int seconds = 5, int intervalMs = 1000) =>
            {
                seconds = Math.Clamp(seconds, 1, 300);
                intervalMs = Math.Clamp(intervalMs, 50, 10_000);
                context.Response.Headers.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";
                context.Response.Headers["X-Accel-Buffering"] = "no";
                var started = DateTime.UtcNow;
                var n = 0;
                while ((DateTime.UtcNow - started).TotalSeconds < seconds && !context.RequestAborted.IsCancellationRequested)
                {
                    await context.Response.WriteAsync(
                        $"event: tick\ndata: {{\"n\":{n++},\"at\":\"{DateTime.UtcNow:O}\"}}\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    await Task.Delay(intervalMs, context.RequestAborted);
                }
                await context.Response.WriteAsync("event: done\ndata: {}\n\n", context.RequestAborted);
            });
        }

        return app;
    }
}

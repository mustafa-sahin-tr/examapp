using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace AssistantService.Endpoints;

public static class AssistantEndpoints
{
    public static WebApplication MapAssistantEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(AssistantGate.PathPrefix).RequireAuthorization();

        // The panel (#507) asks this on every page before showing itself. The gate lets it through for any signed-in user
        // (only 401 without a token), so a closed assistant is 200 + enabled: false instead of a console error.
        group.MapGet("/status", (ClaimsPrincipal user, IOptionsMonitor<AssistantOptions> options, IConfiguration configuration) =>
        {
            var o = options.CurrentValue;
            var gate = AssistantGate.Evaluate(user, o, AssistantGate.ApiKeyConfigured(configuration));
            return gate == AssistantGateResult.Allowed
                ? Results.Ok(new
                {
                    enabled = true,
                    code = (string?)null,
                    roles = o.Roles.Where(user.IsInRole).ToArray(),
                    limits = new { o.MessagesPerHour, o.ImageExplanationsPerDay },
                })
                : Results.Ok(new { enabled = false, code = AssistantGate.Code(gate), roles = Array.Empty<string>(), limits = (object?)null });
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

using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Ocelot'un WebSocket kolu (UseWebSockets=true route'lar) AuthenticationOptions'ı uygulamıyor;
/// token'sız upgrade doğrudan backend'e proxylenip gateway'de 500'e dönüşüyordu.
/// Bu middleware Ocelot'tan ÖNCE çalışır:
///  - AllowedHubPaths dışındaki WebSocket upgrade isteği 400 ile reddedilir (yalnız iki SignalR hub'ı WS kullanır;
///    başka route'lar WS'e açık olsaydı auth atlanırdı).
///  - İzinli hub upgrade isteğinde Bearer doğrulanır; başarısızsa 401 + WWW-Authenticate (Challenge).
/// </summary>
public static class HubWebSocketAuthExtensions
{
    // Tam yol allowlist'i: yalnız bu iki SignalR hub'ı WebSocket upgrade alabilir.
    public static readonly string[] AllowedHubPaths = ["/hub/badges", "/hub/whiteboard"];

    public static IApplicationBuilder UseHubWebSocketAuth(this IApplicationBuilder app, string scheme = "Bearer")
    {
        return app.Use(async (context, next) =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                if (!AllowedHubPaths.Any(p => context.Request.Path.StartsWithSegments(p)))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var result = await context.AuthenticateAsync(scheme);
                if (!result.Succeeded)
                {
                    await context.ChallengeAsync(scheme);
                    return;
                }

                context.User = result.Principal!;
            }

            await next();
        });
    }
}

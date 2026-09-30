using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Ocelot'un WebSocket kolu (UseWebSockets=true route'lar) AuthenticationOptions'ı uygulamıyor;
/// token'sız upgrade doğrudan backend'e proxylenip gateway'de 500'e dönüşüyordu.
/// Bu middleware Ocelot'tan ÖNCE çalışır:
///  - /hub/* dışındaki WebSocket upgrade isteği 400 ile reddedilir (yalnız iki SignalR hub'ı WS kullanır;
///    başka route'lar WS'e açık olsaydı auth atlanırdı).
///  - /hub/* upgrade isteğinde Bearer doğrulanır; başarısızsa 401 + WWW-Authenticate (Challenge).
/// </summary>
public static class HubWebSocketAuthExtensions
{
    public const string HubPathPrefix = "/hub";

    public static IApplicationBuilder UseHubWebSocketAuth(this IApplicationBuilder app, string scheme = "Bearer")
    {
        return app.Use(async (context, next) =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                if (!context.Request.Path.StartsWithSegments(HubPathPrefix))
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

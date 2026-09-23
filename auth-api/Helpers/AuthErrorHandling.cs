using Microsoft.AspNetCore.Diagnostics;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Global güvenlik ağı (issue #231): işlenmemiş exception'lar hiçbir ortamda (Development dahil)
/// yanıt gövdesine stack trace / exception tipi / mesaj olarak yazılmaz. Exception
/// <see cref="ExceptionHandlerMiddleware"/> tarafından log'a yazılır; istemciye yalnızca genel bir
/// ProblemDetails (status + title + traceId) döner.
///
/// Neden Development'ta da: WebApplication Development'ta DeveloperExceptionPage'i örtük ekler ve bu
/// sayfa tam stack trace + istek header'larını döndürür; gateway arkasında bu istemciye kadar ulaşıyordu.
/// <see cref="UseAuthErrorHandling"/> ondan daha içte çalışıp exception'ı yakaladığı için geliştirici
/// sayfası artık devreye girmez. Swagger vb. Development araçları etkilenmez.
/// </summary>
public static class AuthErrorHandling
{
    public static IServiceCollection AddAuthErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                // Yalnızca exception kaynaklı yanıtlar sadeleştirilir; controller'ların bilinçli
                // döndürdüğü Problem(detail: ...) yanıtları olduğu gibi kalır.
                if (context.Exception is null)
                {
                    return;
                }

                context.ProblemDetails.Detail = null;
                context.ProblemDetails.Extensions.Remove("exception");
            };
        });

        return services;
    }

    public static IApplicationBuilder UseAuthErrorHandling(this IApplicationBuilder app)
    {
        return app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            // Controller'da ele alınmayan Keycloak token hataları (ör. /refresh-token) için de doğru
            // durum kodu: kötü/eskimiş grant → 401, Keycloak erişilemez → 503, diğer her şey → 500.
            StatusCodeSelector = exception => exception switch
            {
                KeycloakException { Kind: KeycloakFailureKind.InvalidGrant } => StatusCodes.Status401Unauthorized,
                KeycloakException { Kind: KeycloakFailureKind.ProviderUnavailable } => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status500InternalServerError,
            },
            // Sınıflandırılmış Keycloak hataları beklenen durumlar (kötü grant, Keycloak kesintisi): middleware'in
            // Error seviyesindeki "unhandled exception" log'u + diagnostics bastırılır, yerine Warning yazılır.
            // Diğer her exception Error olarak log'lanmaya devam eder.
            SuppressDiagnosticsCallback = context =>
            {
                if (context.Exception is not KeycloakException { Kind: KeycloakFailureKind.InvalidGrant or KeycloakFailureKind.ProviderUnavailable } keycloakException)
                {
                    return false;
                }

                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(AuthErrorHandling).FullName!)
                    .LogWarning(keycloakException, "Keycloak {Kind} on {Method} {Path}",
                        keycloakException.Kind, context.HttpContext.Request.Method, context.HttpContext.Request.Path);
                return true;
            },
        });
    }
}

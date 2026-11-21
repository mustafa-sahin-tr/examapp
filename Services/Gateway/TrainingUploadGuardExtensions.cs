using System.Security.Claims;
using System.Threading.RateLimiting;

/// <summary>
/// Issue #461: question-detector <c>send-to-fix</c> (eğitim veri setine tam sayfa + kırpmalar yazar) için kullanıcı başına
/// hız sınırı ve gönderen kaydı. <see cref="RouteRoleAuthExtensions"/>'tan SONRA çalışır: yol Admin / QuestionTrainer'a
/// açıktır ve <c>context.User</c> doğrulanmış token'dır.
/// <list type="bullet">
/// <item><c>/question-detector-dev/**</c> isteklerinde istemcinin gönderdiği <see cref="SubmittedByHeader"/> her zaman silinir.</item>
/// <item>POST <c>send-to-fix</c>: token'ın <c>sub</c>'ı başlığa yazılır (detector manifest'e kaydeder) ve
/// <c>RateLimiting:TrainingUpload:PermitLimit</c> / <c>WindowSeconds</c> (varsayılan 10 dakikada 60) aşılırsa 429.</item>
/// </list>
/// </summary>
public static class TrainingUploadGuardExtensions
{
    public const string SubmittedByHeader = "X-Examapp-User";
    public const int DefaultPermitLimit = 60;
    public const int DefaultWindowSeconds = 600;

    private static readonly PathString DetectorPrefix = "/question-detector-dev";

    public static IApplicationBuilder UseTrainingUploadGuard(this IApplicationBuilder app, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimiting:TrainingUpload:PermitLimit", DefaultPermitLimit);
        var windowSeconds = configuration.GetValue("RateLimiting:TrainingUpload:WindowSeconds", DefaultWindowSeconds);
        if (permitLimit < 1) throw new InvalidOperationException("RateLimiting:TrainingUpload:PermitLimit must be >= 1.");
        if (windowSeconds < 1) throw new InvalidOperationException("RateLimiting:TrainingUpload:WindowSeconds must be >= 1.");

        var limiter = PartitionedRateLimiter.Create<string, string>(user =>
            RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

        return app.Use(async (context, next) =>
        {
            var path = new PathString(CollapseSlashes(context.Request.Path.Value ?? ""));
            if (!path.StartsWithSegments(DetectorPrefix, out var remainder))
            {
                await next();
                return;
            }

            // İstemci kendi adına başka birini yazamasın: başlığı yalnız gateway doldurur.
            context.Request.Headers.Remove(SubmittedByHeader);

            var isSendToFix = HttpMethods.IsPost(context.Request.Method)
                && string.Equals(remainder.Value?.Trim('/'), "send-to-fix", StringComparison.OrdinalIgnoreCase);
            if (!isSendToFix)
            {
                await next();
                return;
            }

            var subject = context.User.FindFirstValue("sub") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(subject))
            {
                // Rol kapısı zaten doğrulanmış token istiyor; sub'sız token'a eğitim verisi yazdırılmaz.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var lease = limiter.AttemptAcquire(subject);
            if (!lease.IsAcquired)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = windowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return;
            }

            context.Request.Headers[SubmittedByHeader] = subject;
            await next();
        });
    }

    private static string CollapseSlashes(string p)
    {
        while (p.Contains("//")) p = p.Replace("//", "/");
        return p;
    }
}

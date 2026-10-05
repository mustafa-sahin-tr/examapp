using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Helpers;

/// <summary>
/// <c>RateLimiting:DailyQuestions</c> ayarları (issue #99, security review D4). Varsayılan: öğrenci (Keycloak <c>sub</c>) başına
/// dakikada 30 istek — kart yükleme/yenileme ve "Başla" temposunu etkilemez; set üretim/start uçlarının (havuz sorgusu,
/// transaction) döngüyle zorlanmasını sınırlar.
/// </summary>
public sealed class DailyQuestionsRateLimitOptions
{
    public const string SectionName = "RateLimiting:DailyQuestions";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 30;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// <c>GET api/practice/daily</c> ve <c>POST api/practice/daily/start</c> için sub başına sabit pencere rate limit (issue #99).
/// İki uç TEK kovayı paylaşır. <see cref="WorksheetCommentReadRateLimiting"/> ile birebir aynı desen: dağıtık sayaç
/// (<see cref="IFixedWindowCounterStore"/>), sub'sız istek 401, 429 + Retry-After + <c>{ message, errorCode: "RateLimited" }</c>.
/// </summary>
public static class DailyQuestionsRateLimiting
{
    public const string Policy = "daily-questions";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır.</summary>
    public static IServiceCollection AddDailyQuestionsRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<DailyQuestionsRateLimitOptions>()
            .BindConfiguration(DailyQuestionsRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, DailyQuestionsRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına DAĞITIK sabit pencere + policy'ye özgü 429 metni (<c>practice.dailyRateLimited</c>).</summary>
public sealed class DailyQuestionsRateLimitPolicy : IRateLimiterPolicy<string>
{
    internal const string KeyPrefix = "ratelimit:" + DailyQuestionsRateLimiting.Policy + ":";

    internal const string MissingSubPartitionKey = "sub:<missing>";

    private readonly IOptionsMonitor<DailyQuestionsRateLimitOptions> _options;
    private readonly IFixedWindowCounterStore _store;
    private readonly string _instanceName;
    private readonly ILogger<DailyQuestionsRateLimitPolicy> _logger;

    public DailyQuestionsRateLimitPolicy(
        IOptionsMonitor<DailyQuestionsRateLimitOptions> options,
        IFixedWindowCounterStore store,
        IConfiguration configuration,
        ILogger<DailyQuestionsRateLimitPolicy> logger)
    {
        _options = options;
        _store = store;
        _instanceName = configuration["Redis:InstanceName"] ?? string.Empty;
        _logger = logger;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        if (string.IsNullOrEmpty(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)))
            return RateLimitPartition.Get(MissingSubPartitionKey, _ => new RejectAllRateLimiter());

        var settings = _options.CurrentValue;
        var partitionKey = AdminUserListRateLimiting.PartitionKey(httpContext);
        var storeKey = _instanceName + KeyPrefix + partitionKey;
        return RateLimitPartition.Get(partitionKey, _ => new DistributedFixedWindowRateLimiter(
            _store, storeKey, settings.PermitLimit, TimeSpan.FromSeconds(settings.WindowSeconds)));
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    private async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)))
        {
            _logger.LogWarning("[DailyQuestions] sub claim'i olmayan istek reddedildi: path={Path}",
                context.HttpContext.Request.Path.Value);
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        _logger.LogWarning("[DailyQuestions] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "practice.dailyRateLimited", _options.CurrentValue.WindowSeconds, "RateLimited", cancellationToken);
    }
}

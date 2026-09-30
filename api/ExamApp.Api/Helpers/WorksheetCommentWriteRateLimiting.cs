using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.WorksheetComments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Helpers;

/// <summary>
/// <c>RateLimiting:WorksheetCommentWrite</c> ayarları (issue #105). Varsayılan: kullanıcı başına dakikada 10 yorum —
/// elle yazılan yorum/cevap temposunu etkilemez, spam/otomasyonu sınırlar.
/// </summary>
public sealed class WorksheetCommentWriteRateLimitOptions
{
    public const string SectionName = "RateLimiting:WorksheetCommentWrite";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 10;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Yorum yazma ucuna (<c>POST api/worksheet/{worksheetId}/comments</c>) KULLANICI (Keycloak <c>sub</c>) başına sabit
/// pencere rate limit (issue #105). Amaç: herkese açık thread'e spam'i ve (dilim 2'de) bildirim selini sınırlamak.
/// <see cref="TeacherActivityRateLimiting"/> (#265) ile birebir aynı desen.
///
/// <see cref="AdminUserListRateLimiting"/> (#246/#262) altyapısını yeniden kullanır: aynı <see cref="IFixedWindowCounterStore"/>
/// (Redis varsa dağıtık — tüm replica'lar ortak sayaç; kesintide fail-open; <c>Redis:Configuration</c> boşsa süreç içi) ve
/// <see cref="DistributedFixedWindowRateLimiter"/>. Sub'sız istek koşulsuz 401 (ortak kova yok, #279 item 7).
/// 429 + Retry-After + JSON <c>{ message, errorCode: "RateLimited" }</c> (#309; message = yerelleştirilmiş <c>worksheets.comments.rateLimited</c>).
/// </summary>
public static class WorksheetCommentWriteRateLimiting
{
    public const string Policy = "worksheet-comment-write";

    /// <summary>
    /// <see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global 429 ayarları ve
    /// <see cref="IFixedWindowCounterStore"/> kaydı orada).
    /// </summary>
    public static IServiceCollection AddWorksheetCommentWriteRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<WorksheetCommentWriteRateLimitOptions>()
            .BindConfiguration(WorksheetCommentWriteRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, WorksheetCommentWriteRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına DAĞITIK sabit pencere + policy'ye özgü 429 metni (<c>worksheets.comments.rateLimited</c>).</summary>
public sealed class WorksheetCommentWriteRateLimitPolicy : IRateLimiterPolicy<string>
{
    /// <summary>Redis anahtar öneki: <c>{Redis:InstanceName}ratelimit:worksheet-comment-write:sub:{sub}</c>.</summary>
    internal const string KeyPrefix = "ratelimit:" + WorksheetCommentWriteRateLimiting.Policy + ":";

    internal const string MissingSubPartitionKey = "sub:<missing>";

    private readonly IOptionsMonitor<WorksheetCommentWriteRateLimitOptions> _options;
    private readonly IFixedWindowCounterStore _store;
    private readonly string _instanceName;
    private readonly ILogger<WorksheetCommentWriteRateLimitPolicy> _logger;

    public WorksheetCommentWriteRateLimitPolicy(
        IOptionsMonitor<WorksheetCommentWriteRateLimitOptions> options,
        IFixedWindowCounterStore store,
        IConfiguration configuration,
        ILogger<WorksheetCommentWriteRateLimitPolicy> logger)
    {
        _options = options;
        _store = store;
        _instanceName = configuration["Redis:InstanceName"] ?? string.Empty;
        _logger = logger;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // Sub'sız istekler tek ortak kovayı paylaşıp birbirini kilitlemesin diye koşulsuz reddedilir (#243 review, #279).
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
            // Kota aşımı değil kimlik eksikliği: 429 + Retry-After yanıltıcı olurdu.
            _logger.LogWarning("[WorksheetCommentWrite] sub claim'i olmayan istek reddedildi: path={Path}",
                context.HttpContext.Request.Path.Value);
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        _logger.LogWarning("[WorksheetCommentWrite] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "worksheets.comments.rateLimited", _options.CurrentValue.WindowSeconds,
            WorksheetCommentErrorCodes.RateLimited, cancellationToken);
    }
}

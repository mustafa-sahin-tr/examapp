using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.DirectMessages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Helpers;

/// <summary>Doğrudan mesaj rate limit ayarlarının ortak şekli (issue #106).</summary>
public abstract class DirectMessageRateLimitOptionsBase
{
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; }

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; }

    /// <summary>Kova adı (Redis anahtar öneki de bundan türer). Yapılandırmadan bağlanmaz.</summary>
    internal abstract string PolicyName { get; }

    /// <summary>429 gövdesindeki yerelleştirilmiş mesaj anahtarı.</summary>
    internal abstract string MessageKey { get; }
}

/// <summary><c>RateLimiting:DirectMessageSend</c> — mesaj gönderme. Varsayılan: kullanıcı başına dakikada 10.</summary>
public sealed class DirectMessageSendRateLimitOptions : DirectMessageRateLimitOptionsBase
{
    public const string SectionName = "RateLimiting:DirectMessageSend";
    public DirectMessageSendRateLimitOptions() { PermitLimit = 10; WindowSeconds = 60; }
    internal override string PolicyName => DirectMessageRateLimiting.SendPolicy;
    internal override string MessageKey => "directMessages.rateLimited";
}

/// <summary><c>RateLimiting:DirectMessageReport</c> — şikayet. Varsayılan: kullanıcı başına saatte 20 (BA taslağı).</summary>
public sealed class DirectMessageReportRateLimitOptions : DirectMessageRateLimitOptionsBase
{
    public const string SectionName = "RateLimiting:DirectMessageReport";
    public DirectMessageReportRateLimitOptions() { PermitLimit = 20; WindowSeconds = 3600; }
    internal override string PolicyName => DirectMessageRateLimiting.ReportPolicy;
    internal override string MessageKey => "directMessages.reportRateLimited";
}

/// <summary><c>RateLimiting:DirectMessageRead</c> — liste/gelen kutusu/mesaj geçmişi/okundu. Varsayılan: dakikada 60.</summary>
public sealed class DirectMessageReadRateLimitOptions : DirectMessageRateLimitOptionsBase
{
    public const string SectionName = "RateLimiting:DirectMessageRead";
    public DirectMessageReadRateLimitOptions() { PermitLimit = 60; WindowSeconds = 60; }
    internal override string PolicyName => DirectMessageRateLimiting.ReadPolicy;
    internal override string MessageKey => "directMessages.readRateLimited";
}

/// <summary><c>RateLimiting:DirectMessageBlock</c> — engelle/kaldır (security O2: gönderimden ayrı kova). Varsayılan: saatte 30.</summary>
public sealed class DirectMessageBlockRateLimitOptions : DirectMessageRateLimitOptionsBase
{
    public const string SectionName = "RateLimiting:DirectMessageBlock";
    public DirectMessageBlockRateLimitOptions() { PermitLimit = 30; WindowSeconds = 3600; }
    internal override string PolicyName => DirectMessageRateLimiting.BlockPolicy;
    internal override string MessageKey => "directMessages.blockRateLimited";
}

/// <summary>
/// <c>RateLimiting:DirectMessageSearch</c> — öğretmen listesinde ad araması (her arama auth-api'ye ≤ 200 id'lik batch gider;
/// security D5). Varsayılan: dakikada 20.
/// </summary>
public sealed class DirectMessageSearchRateLimitOptions : DirectMessageRateLimitOptionsBase
{
    public const string SectionName = "RateLimiting:DirectMessageSearch";
    public DirectMessageSearchRateLimitOptions() { PermitLimit = 20; WindowSeconds = 60; }
    internal override string PolicyName => DirectMessageRateLimiting.SearchPolicy;
    internal override string MessageKey => "directMessages.searchRateLimited";
}

/// <summary>
/// issue #106: doğrudan mesaj uçlarına KULLANICI (Keycloak <c>sub</c>) başına sabit pencere rate limit — ayrı kovalar (gönderme,
/// şikayet, okuma, engel, arama). <see cref="WorksheetCommentWriteRateLimiting"/> (#105) ile aynı altyapı:
/// <see cref="IFixedWindowCounterStore"/> (Redis varsa dağıtık, kesintide fail-open; yoksa süreç içi) ve
/// <see cref="DistributedFixedWindowRateLimiter"/>; sub'sız istek koşulsuz 401; 429 + Retry-After + JSON
/// <c>{ message, errorCode: "RateLimited" }</c>. Yeni konuşma (günlük) ve konuşma başına (saatlik) kotalar HTTP kovası değil,
/// servis içinde DB'den sayılır (<see cref="DirectMessageQuotaOptions"/>).
/// </summary>
public static class DirectMessageRateLimiting
{
    public const string SendPolicy = "direct-message-send";
    public const string ReportPolicy = "direct-message-report";
    public const string ReadPolicy = "direct-message-read";
    public const string BlockPolicy = "direct-message-block";
    public const string SearchPolicy = "direct-message-search";

    /// <summary>
    /// Öğretmen listesi ucu: <c>search</c> parametresi doluysa arama kovası (<see cref="SearchPolicy"/>), değilse okuma kovası
    /// (<see cref="ReadPolicy"/>; diğer okuma uçlarıyla AYNI sayaç).
    /// </summary>
    public const string TeacherListPolicy = "direct-message-teacher-list";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (sayaç deposu orada).</summary>
    public static IServiceCollection AddDirectMessageRateLimiting(this IServiceCollection services)
    {
        Add<DirectMessageSendRateLimitOptions>(services, DirectMessageSendRateLimitOptions.SectionName, SendPolicy);
        Add<DirectMessageReportRateLimitOptions>(services, DirectMessageReportRateLimitOptions.SectionName, ReportPolicy);
        Add<DirectMessageReadRateLimitOptions>(services, DirectMessageReadRateLimitOptions.SectionName, ReadPolicy);
        Add<DirectMessageBlockRateLimitOptions>(services, DirectMessageBlockRateLimitOptions.SectionName, BlockPolicy);
        Add<DirectMessageSearchRateLimitOptions>(services, DirectMessageSearchRateLimitOptions.SectionName, SearchPolicy);
        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, DirectMessageTeacherListRateLimitPolicy>(TeacherListPolicy));

        services.AddOptions<DirectMessageQuotaOptions>()
            .BindConfiguration(DirectMessageQuotaOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }

    private static void Add<TOptions>(IServiceCollection services, string section, string policy)
        where TOptions : DirectMessageRateLimitOptionsBase, new()
    {
        services.AddOptions<TOptions>()
            .BindConfiguration(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, DirectMessageRateLimitPolicy<TOptions>>(policy));
    }
}

/// <summary>Kova bölümü + red yazımı (tüm doğrudan mesaj policy'leri için ortak).</summary>
internal static class DirectMessageRateLimitBuckets
{
    internal const string MissingSubPartitionKey = "sub:<missing>";

    internal static bool HasSub(HttpContext httpContext)
        => !string.IsNullOrEmpty(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));

    internal static RateLimitPartition<string> Partition(
        HttpContext httpContext, DirectMessageRateLimitOptionsBase settings, IFixedWindowCounterStore store, string instanceName)
    {
        if (!HasSub(httpContext))
            return RateLimitPartition.Get(MissingSubPartitionKey, _ => new RejectAllRateLimiter());

        // Bölüm anahtarı kovayı içerir (bir policy iki kova seçebilir); Redis anahtarı {Instance}ratelimit:{kova}:sub:{sub} —
        // aynı kovayı kullanan farklı policy'ler aynı sayacı paylaşır.
        var sub = AdminUserListRateLimiting.PartitionKey(httpContext);
        var storeKey = instanceName + "ratelimit:" + settings.PolicyName + ":" + sub;
        return RateLimitPartition.Get(settings.PolicyName + ":" + sub, _ => new DistributedFixedWindowRateLimiter(
            store, storeKey, settings.PermitLimit, TimeSpan.FromSeconds(settings.WindowSeconds)));
    }

    internal static async ValueTask RejectAsync(
        OnRejectedContext context, DirectMessageRateLimitOptionsBase settings, ILogger logger, CancellationToken cancellationToken)
    {
        if (!HasSub(context.HttpContext))
        {
            logger.LogWarning("[DirectMessages:{Policy}] sub claim'i olmayan istek reddedildi: path={Path}",
                settings.PolicyName, context.HttpContext.Request.Path.Value);
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        logger.LogWarning("[DirectMessages:{Policy}] Rate limit aşıldı: sub={Sub} path={Path}",
            settings.PolicyName, AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, settings.MessageKey, settings.WindowSeconds, DirectMessageErrorCodes.RateLimited, cancellationToken);
    }
}

/// <summary>Tek kovalı policy (gönderme / şikayet / okuma / engel / arama).</summary>
public sealed class DirectMessageRateLimitPolicy<TOptions> : IRateLimiterPolicy<string>
    where TOptions : DirectMessageRateLimitOptionsBase, new()
{
    private readonly IOptionsMonitor<TOptions> _options;
    private readonly IFixedWindowCounterStore _store;
    private readonly string _instanceName;
    private readonly ILogger<DirectMessageRateLimitPolicy<TOptions>> _logger;

    public DirectMessageRateLimitPolicy(
        IOptionsMonitor<TOptions> options,
        IFixedWindowCounterStore store,
        IConfiguration configuration,
        ILogger<DirectMessageRateLimitPolicy<TOptions>> logger)
    {
        _options = options;
        _store = store;
        _instanceName = configuration["Redis:InstanceName"] ?? string.Empty;
        _logger = logger;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
        => DirectMessageRateLimitBuckets.Partition(httpContext, _options.CurrentValue, _store, _instanceName);

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected
        => (context, ct) => DirectMessageRateLimitBuckets.RejectAsync(context, _options.CurrentValue, _logger, ct);
}

/// <summary>
/// Öğretmen listesi: <c>?search=</c> doluysa arama kovası, değilse okuma kovası (security D5). Boş/yalnız boşluk arama
/// parametresi aramasız sayılır (servisle aynı karar).
/// </summary>
public sealed class DirectMessageTeacherListRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<DirectMessageReadRateLimitOptions> _read;
    private readonly IOptionsMonitor<DirectMessageSearchRateLimitOptions> _search;
    private readonly IFixedWindowCounterStore _store;
    private readonly string _instanceName;
    private readonly ILogger<DirectMessageTeacherListRateLimitPolicy> _logger;

    public DirectMessageTeacherListRateLimitPolicy(
        IOptionsMonitor<DirectMessageReadRateLimitOptions> read,
        IOptionsMonitor<DirectMessageSearchRateLimitOptions> search,
        IFixedWindowCounterStore store,
        IConfiguration configuration,
        ILogger<DirectMessageTeacherListRateLimitPolicy> logger)
    {
        _read = read;
        _search = search;
        _store = store;
        _instanceName = configuration["Redis:InstanceName"] ?? string.Empty;
        _logger = logger;
    }

    private DirectMessageRateLimitOptionsBase Bucket(HttpContext httpContext)
        => string.IsNullOrWhiteSpace(httpContext.Request.Query["search"].ToString()) ? _read.CurrentValue : _search.CurrentValue;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
        => DirectMessageRateLimitBuckets.Partition(httpContext, Bucket(httpContext), _store, _instanceName);

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected
        => (context, ct) => DirectMessageRateLimitBuckets.RejectAsync(context, Bucket(context.HttpContext), _logger, ct);
}

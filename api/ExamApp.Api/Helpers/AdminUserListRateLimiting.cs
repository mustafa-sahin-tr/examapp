using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.RateLimiting;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Helpers;

/// <summary>
/// <c>RateLimiting:AdminUserList</c> ayarları (issue #246). Varsayılan: dakikada 30 sayfa (kullanıcı başına).
/// </summary>
public sealed class AdminUserListRateLimitOptions
{
    public const string SectionName = "RateLimiting:AdminUserList";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 30;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// issue #262: Redis sayaç çağrısının üst süresi (ms). Aşılırsa fail-open (istek geçer, uyarı loglanır) —
    /// yavaş Redis admin listelerini bekletmesin.
    /// </summary>
    [Range(10, 10_000)]
    public int StoreTimeoutMilliseconds { get; set; } = 500;
}

/// <summary>
/// Admin kişisel veri listelerine (<c>GET api/admin/students</c>, <c>GET api/admin/teachers</c>) KULLANICI başına
/// sabit pencere rate limit (issue #246). Amaç: çalınmış bir admin token'ı ile tüm okulların listesinin dakikalar içinde
/// çekilmesini yavaşlatmak ve sayfa başına tetiklenen auth-api/Keycloak yükünü sınırlamak.
///
/// auth-api'deki <c>AuthRateLimiting</c> (#231) deseniyle aynı: isimli policy + <c>[EnableRateLimiting]</c>,
/// 429 düz metin body + <c>Retry-After</c>. Fark: partition anahtarı IP değil Keycloak <c>sub</c> — kimliği doğrulanmış
/// uçlarda IP'yi değiştirmek kolay, token sahibini değiştirmek değil; ayrıca gateway arkasında tüm adminler tek IP'de
/// toplanabilir. İki uç TEK kovayı paylaşır (öğrenci + öğretmen sayfaları toplamda sayılır).
///
/// Pipeline'da UseAuthentication/UseAuthorization'dan SONRA çalışır: 401/403 alan istekler kovayı tüketmez ve
/// <c>User</c> doludur. Ayarlar istek anında <see cref="IOptionsMonitor{T}"/>'tan okunur (partition ilk oluştuğunda).
///
/// issue #262: sayaç artık DAĞITIK — <see cref="IFixedWindowCounterStore"/> (üretimde Redis, tüm replica'lar ortak sayaç;
/// önceden instance başına bellekteydi ve N replica'da etkin limit N×30/dk'ydı). Redis kesintisinde FAIL-OPEN (uyarı logu).
/// <c>Redis:Configuration</c> boşsa süreç içi sayaç (lokal/test). Reddedilen istekler (429) <c>AdminDataAccessLogs</c>'a
/// <c>Outcome=RateLimited</c> olarak da yazılır — pencere başına yalnızca ilk red (kaynak: endpoint'teki <see cref="AdminDataAccessAttribute"/>).
/// Kova artık öğretmen başvurusu listesi + detayını da kapsar (<c>GET api/admin/teacher-applications[/{id}]</c>).
///
/// Reddetme metni policy'ye özgüdür (<see cref="AdminUserListRateLimitPolicy.OnRejected"/>); ileride eklenecek
/// başka policy'ler kendi OnRejected'ını vermezse global handler jenerik <c>common.tooManyRequests</c> metnini yazar.
/// </summary>
public static class AdminUserListRateLimiting
{
    public const string Policy = "admin-user-list";

    public static IServiceCollection AddAdminUserListRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AdminUserListRateLimitOptions>()
            .BindConfiguration(AdminUserListRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // issue #262: dağıtık sayaç. Redis yapılandırılmışsa ortak multiplexer (IRedisConnectionProvider) üzerinden Redis,
        // değilse süreç içi. Testler / özel kurulumlar kendi deposunu önceden kaydedebilir (TryAdd).
        services.TryAddSingleton<IRedisConnectionProvider, RedisConnectionProvider>();
        services.TryAddSingleton<IFixedWindowCounterStore>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            if (string.IsNullOrWhiteSpace(configuration[RedisConnectionProvider.ConfigurationKey]))
            {
                sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminUserListRateLimiting))
                    .LogWarning("[RateLimit] {Key} tanımlı değil — admin veri uçlarının rate limit'i süreç içi (replica başına).",
                        RedisConnectionProvider.ConfigurationKey);
                return new InMemoryFixedWindowCounterStore();
            }

            var timeout = TimeSpan.FromMilliseconds(
                sp.GetRequiredService<IOptions<AdminUserListRateLimitOptions>>().Value.StoreTimeoutMilliseconds);
            return new RedisFixedWindowCounterStore(
                sp.GetRequiredService<IRedisConnectionProvider>(), timeout,
                sp.GetRequiredService<ILogger<RedisFixedWindowCounterStore>>());
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Jenerik yedek: kendi OnRejected'ı olmayan policy'ler için. Policy OnRejected'ı varsa bu ÇAĞRILMAZ.
            options.OnRejected = (context, cancellationToken) =>
                WriteRejectionAsync(context, "common.tooManyRequests", fallbackRetryAfterSeconds: null, cancellationToken);

            options.AddPolicy<string, AdminUserListRateLimitPolicy>(Policy);
        });

        return services;
    }

    /// <summary>429 + (biliniyorsa) Retry-After (saniye) + yerelleştirilmiş düz metin body.</summary>
    internal static async ValueTask WriteRejectionAsync(
        OnRejectedContext context, string messageKey, int? fallbackRetryAfterSeconds, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        int? retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata)
            ? Math.Max(1, (int)Math.Ceiling(metadata.TotalSeconds))
            : fallbackRetryAfterSeconds;
        if (retryAfter is { } seconds)
            response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var localizer = context.HttpContext.RequestServices.GetService<IStringLocalizer<Messages>>()
                        ?? FallbackMessageLocalizer.Instance;
        await response.WriteAsync(localizer[messageKey].Value, cancellationToken);
    }

    /// <summary>
    /// Keycloak <c>sub</c>. [Authorize(Roles="Admin")] nedeniyle normalde hep doludur; olmazsa tüm "kimliksiz"
    /// istekler tek ortak kovaya düşer (limit gevşemez, sıkılaşır).
    /// </summary>
    internal static string PartitionKey(HttpContext httpContext) =>
        "sub:" + (httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown");
}

/// <summary>
/// <see cref="AdminUserListRateLimiting.Policy"/>'nin kendisi: sub başına DAĞITIK sabit pencere (issue #262) + policy'ye özgü
/// 429 metni (<c>admin.userList.rateLimited</c>) + reddin audit'i. DI ile oluşturulur (singleton ömrü; ayarlar partition
/// ilk oluştuğunda okunur).
/// </summary>
public sealed class AdminUserListRateLimitPolicy : IRateLimiterPolicy<string>
{
    /// <summary>Redis anahtar öneki: <c>{Redis:InstanceName}ratelimit:admin-user-list:sub:{sub}</c>.</summary>
    internal const string KeyPrefix = "ratelimit:" + AdminUserListRateLimiting.Policy + ":";

    private readonly IOptionsMonitor<AdminUserListRateLimitOptions> _options;
    private readonly IFixedWindowCounterStore _store;
    private readonly string _instanceName;
    private readonly ILogger<AdminUserListRateLimitPolicy> _logger;

    public AdminUserListRateLimitPolicy(
        IOptionsMonitor<AdminUserListRateLimitOptions> options,
        IFixedWindowCounterStore store,
        IConfiguration configuration,
        ILogger<AdminUserListRateLimitPolicy> logger)
    {
        _options = options;
        _store = store;
        _instanceName = configuration["Redis:InstanceName"] ?? string.Empty;
        _logger = logger;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var settings = _options.CurrentValue;
        var partitionKey = AdminUserListRateLimiting.PartitionKey(httpContext);
        var storeKey = _instanceName + KeyPrefix + partitionKey;
        return RateLimitPartition.Get(partitionKey, _ => new DistributedFixedWindowRateLimiter(
            _store, storeKey, settings.PermitLimit, TimeSpan.FromSeconds(settings.WindowSeconds)));
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    private async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        // Olası token kötüye kullanımının izi: yalnızca sub (PII değil) + path.
        _logger.LogWarning("[AdminUserList] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        // issue #262 review: pencere başına yalnızca İLK red audit'lenir (sonraki 429'lar tabloyu şişirmesin / DB'ye
        // yazma yükü bindirmesin). Log her red için kalır.
        if (context.Lease.TryGetMetadata(DistributedFixedWindowRateLimiter.FirstRejectionMetadataName, out var first) && first is true)
            await AuditRejectionAsync(context.HttpContext);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "admin.userList.rateLimited", _options.CurrentValue.WindowSeconds, cancellationToken);
    }

    /// <summary>
    /// issue #262: 429'u kötüye kullanım incelemesi için <c>AdminDataAccessLogs</c>'a yazar (<c>Outcome=RateLimited</c>).
    /// Best-effort: yazılamazsa uyarı loglanır, 429 yine döner (veri dönmediği için fail-closed gerekmez).
    /// </summary>
    private async Task AuditRejectionAsync(HttpContext httpContext)
    {
        var resource = httpContext.GetEndpoint()?.Metadata.GetMetadata<AdminDataAccessAttribute>()?.Resource;
        if (resource is null)
        {
            _logger.LogWarning("[AdminUserList] 429 audit'lenemedi: endpoint'te [AdminDataAccess] yok (path={Path}).",
                httpContext.Request.Path.Value);
            return;
        }

        try
        {
            var audit = httpContext.RequestServices.GetService<IAdminDataAccessAuditService>();
            if (audit is null)
                return;

            var query = httpContext.Request.Query;
            int? schoolId = int.TryParse(query["schoolId"], NumberStyles.None, CultureInfo.InvariantCulture, out var sid) && sid > 0
                ? sid : null;
            var unassigned = bool.TryParse(query["unassigned"], out var u) && u;
            int? targetId = int.TryParse(httpContext.GetRouteValue("id") as string, NumberStyles.None, CultureInfo.InvariantCulture, out var tid)
                ? tid : null;

            await audit.RecordRateLimitedAsync(new AdminRateLimitedAccessRecord(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                resource.Value, schoolId, unassigned, targetId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminUserList] 429 audit satırı yazılamadı (path={Path}).", httpContext.Request.Path.Value);
        }
    }
}

/// <summary>
/// issue #262: sayacı <see cref="IFixedWindowCounterStore"/>'da (Redis) tutan <see cref="RateLimiter"/>. Yerel durum yoktur;
/// partition başına bir örnek (sub), boşta kalınca <c>PartitionedRateLimiter</c> tarafından atılır.
/// Senkron <see cref="AttemptAcquireCore"/> I/O yapamaz → her zaman "hemen değil" döner; rate limiting middleware ardından
/// <see cref="AcquireAsyncCore"/>'u çağırır ve asıl karar orada Redis'ten verilir.
/// </summary>
internal sealed class DistributedFixedWindowRateLimiter : RateLimiter
{
    /// <summary>
    /// Lease metadata'sı: bu red pencerede limiti İLK aşan istek mi (<see cref="bool"/>). 429 audit'i yalnızca bunda yazılır.
    /// </summary>
    internal const string FirstRejectionMetadataName = "ExamApp.FirstRejection";

    private static readonly RateLimitLease Acquired = new Lease(true, null, false);
    private static readonly RateLimitLease NotYet = new Lease(false, null, false);

    private readonly IFixedWindowCounterStore _store;
    private readonly string _key;
    private readonly int _permitLimit;
    private readonly TimeSpan _window;
    private long _lastUsedTimestamp = Stopwatch.GetTimestamp();

    public DistributedFixedWindowRateLimiter(IFixedWindowCounterStore store, string key, int permitLimit, TimeSpan window)
    {
        _store = store;
        _key = key;
        _permitLimit = permitLimit;
        _window = window;
    }

    public override TimeSpan? IdleDuration => Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastUsedTimestamp));

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => permitCount == 0 ? Acquired : NotYet;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        if (permitCount == 0)
            return Acquired;

        Interlocked.Exchange(ref _lastUsedTimestamp, Stopwatch.GetTimestamp());
        var decision = await _store.TryAcquireAsync(_key, permitCount, _permitLimit, _window, cancellationToken);
        return decision.Allowed
            ? Acquired
            : new Lease(false, decision.RetryAfter, decision.IsFirstRejection(permitCount, _permitLimit));
    }

    private sealed class Lease : RateLimitLease
    {
        private readonly TimeSpan? _retryAfter;
        private readonly bool _firstRejection;

        public Lease(bool isAcquired, TimeSpan? retryAfter, bool firstRejection)
        {
            IsAcquired = isAcquired;
            _retryAfter = retryAfter;
            _firstRejection = firstRejection;
        }

        public override bool IsAcquired { get; }

        public override IEnumerable<string> MetadataNames
        {
            get
            {
                if (_retryAfter.HasValue)
                    yield return MetadataName.RetryAfter.Name;
                if (!IsAcquired)
                    yield return FirstRejectionMetadataName;
            }
        }

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (_retryAfter.HasValue && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = _retryAfter.Value;
                return true;
            }

            if (!IsAcquired && metadataName == FirstRejectionMetadataName)
            {
                metadata = _firstRejection;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}

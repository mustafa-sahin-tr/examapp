using System;
using System.ComponentModel.DataAnnotations;
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
/// <c>RateLimiting:ParentInvitationWrite</c> — issue #440: öğrencinin "veli bilgisi ekle / e-postayı düzelt" ve "yeniden gönder"
/// istekleri (her biri auth-api'de hesap arar/açar ya da e-posta event'i yazar). Varsayılan: öğrenci (sub) başına saatte 10 — iş
/// kuralları (1 saatte 3 farklı e-posta, 24 saatte 1 gönderim) servis katmanında ayrıca uygulanır.
/// </summary>
public sealed class ParentInvitationWriteRateLimitOptions : ParentLinkRateLimitOptionsBase
{
    public const string SectionName = "RateLimiting:ParentInvitationWrite";
    public ParentInvitationWriteRateLimitOptions() { PermitLimit = 10; WindowSeconds = 3600; }
    internal override string PolicyName => ParentInvitationRateLimiting.WritePolicy;
    internal override string MessageKey => "parentInvitations.writeRateLimited";
}

/// <summary>
/// <c>RateLimiting:ParentActivation</c> — issue #440: anonim aktivasyon uçları (lookup / activate / resend). İstemci IP'si başına
/// (<see cref="TrustedClientIp"/>: güvenilir proxy'den gelen <c>X-Forwarded-For</c>, aksi bağlantı adresi) varsayılan 10 dakikada 20. Token 256 bit olduğundan tahmin edilemez; sınır
/// parola deneme / e-posta tetikleme akışını keser. Gateway giriş IP kovası da sayar.
/// </summary>
public sealed class ParentActivationRateLimitOptions
{
    public const string SectionName = "RateLimiting:ParentActivation";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 20;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 600;
}

/// <summary>Issue #440 rate limit kayıtları.</summary>
public static class ParentInvitationRateLimiting
{
    public const string WritePolicy = "parent-invitation-write";
    public const string ActivationPolicy = "parent-activation";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (sayaç deposu orada).</summary>
    public static IServiceCollection AddParentInvitationRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<ParentInvitationWriteRateLimitOptions>()
            .BindConfiguration(ParentInvitationWriteRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, ParentLinkRateLimitPolicy<ParentInvitationWriteRateLimitOptions>>(WritePolicy));

        services.AddOptions<ParentActivationRateLimitOptions>()
            .BindConfiguration(ParentActivationRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, ParentActivationRateLimitPolicy>(ActivationPolicy));
        return services;
    }
}

/// <summary>Anonim aktivasyon uçları: istemci IP'si başına dağıtık sabit pencere; 429 + Retry-After + <c>{ message, errorCode }</c>.</summary>
public sealed class ParentActivationRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<ParentActivationRateLimitOptions> _options;
    private readonly IFixedWindowCounterStore _store;
    private readonly string _instanceName;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ParentActivationRateLimitPolicy> _logger;

    public ParentActivationRateLimitPolicy(
        IOptionsMonitor<ParentActivationRateLimitOptions> options,
        IFixedWindowCounterStore store,
        IConfiguration configuration,
        ILogger<ParentActivationRateLimitPolicy> logger)
    {
        _options = options;
        _store = store;
        _instanceName = configuration["Redis:InstanceName"] ?? string.Empty;
        _configuration = configuration;
        _logger = logger;
    }

    // Security MINOR2: ilk X-Forwarded-For'a koşulsuz güvenilmez — yalnız güvenilir proxy'den gelen başlık (TrustedClientIp).
    private string ClientKey(HttpContext httpContext) => TrustedClientIp.Resolve(httpContext, _configuration);

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var settings = _options.CurrentValue;
        var ip = ClientKey(httpContext);
        var storeKey = _instanceName + "ratelimit:" + ParentInvitationRateLimiting.ActivationPolicy + ":" + ip;
        return RateLimitPartition.Get(ParentInvitationRateLimiting.ActivationPolicy + ":" + ip, _ => new DistributedFixedWindowRateLimiter(
            _store, storeKey, settings.PermitLimit, TimeSpan.FromSeconds(settings.WindowSeconds)));
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    private async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        _logger.LogWarning("[ParentActivation] Rate limit aşıldı: ip={Ip} path={Path}",
            ClientKey(context.HttpContext), context.HttpContext.Request.Path.Value);
        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "parentInvitations.activationRateLimited", _options.CurrentValue.WindowSeconds,
            ParentLinkRateLimiting.RateLimitedErrorCode, cancellationToken);
    }
}

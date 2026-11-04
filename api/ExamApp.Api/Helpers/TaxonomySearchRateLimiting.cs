using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Helpers;

/// <summary><c>RateLimiting:TaxonomySearch</c> ayarları (issue #499). Varsayılan: kullanıcı başına dakikada 60 arama.</summary>
public sealed class TaxonomySearchRateLimitOptions
{
    public const string SectionName = "RateLimiting:TaxonomySearch";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 60;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Issue #499: her arama taksonomi adlarını okur ve bellekte eşler. Yazarken arama yapan kutu ya da asistan döngüsü DB'yi
/// yormasın diye kullanıcı (sub) başına sabit pencere. <see cref="ActivityCheckRateLimiting"/> ile aynı desen.
/// </summary>
public static class TaxonomySearchRateLimiting
{
    public const string Policy = "taxonomy-search";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddTaxonomySearchRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<TaxonomySearchRateLimitOptions>()
            .BindConfiguration(TaxonomySearchRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, TaxonomySearchRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + 429 <c>{ message, errorCode: "RateLimited" }</c>.</summary>
public sealed class TaxonomySearchRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<TaxonomySearchRateLimitOptions> _options;
    private readonly ILogger<TaxonomySearchRateLimitPolicy> _logger;

    public TaxonomySearchRateLimitPolicy(
        IOptionsMonitor<TaxonomySearchRateLimitOptions> options, ILogger<TaxonomySearchRateLimitPolicy> logger)
    {
        _options = options;
        _logger = logger;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var settings = _options.CurrentValue;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: AdminUserListRateLimiting.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.PermitLimit,
                Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    private async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        _logger.LogWarning("[TaxonomySearch] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "taxonomy.search.rateLimited", _options.CurrentValue.WindowSeconds, errorCode: "RateLimited", cancellationToken);
    }
}

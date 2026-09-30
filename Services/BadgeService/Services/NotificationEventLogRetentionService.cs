using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BadgeService.Services;

/// <summary>
/// <c>NotificationEventLogRetention</c> config bölümü (issue #305 review): <c>NotificationEventLogs</c> yalnız ekleme alan
/// bir idempotency defteri. Desen <see cref="ProcessedAnswerSubmissionRetentionOptions"/> ile aynı; saklama süresi ve tarama
/// aralığı ayarlanabilir.
/// </summary>
public sealed class NotificationEventLogRetentionOptions
{
    public const string SectionName = "NotificationEventLogRetention";

    /// <summary>Bu kadar günden eski satırlar silinir. Varsayılan: 30 gün (yeniden teslim penceresinden çok uzun).</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 30;

    [Range(1, 100_000)]
    public int DeleteBatchSize { get; set; } = 5_000;

    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);
}

public interface INotificationEventLogRetentionJob
{
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}

/// <summary>
/// Saklama süresini aşan <c>NotificationEventLog</c> satırlarını partiler halinde <c>ExecuteDelete</c> ile siler. Satır açan
/// ilk event ayrıca <c>Notification.(Type, SourceEventId)</c> ile korunduğundan, silinen log yalnız BİRLEŞTİRİLEN eski event'lerin
/// 30 günden sonraki teslimini etkiler (RabbitMQ yeniden teslim penceresi dışı).
/// </summary>
public sealed class NotificationEventLogRetentionJob : INotificationEventLogRetentionJob
{
    internal const int MaxBatchesPerRun = 1_000;

    private readonly BadgeDbContext _context;
    private readonly IOptionsMonitor<NotificationEventLogRetentionOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<NotificationEventLogRetentionJob> _logger;

    public NotificationEventLogRetentionJob(
        BadgeDbContext context,
        IOptionsMonitor<NotificationEventLogRetentionOptions> options,
        TimeProvider? clock = null,
        ILogger<NotificationEventLogRetentionJob>? logger = null)
    {
        _context = context;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NotificationEventLogRetentionJob>.Instance;
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var settings = _options.CurrentValue;
        var cutoff = _clock.GetUtcNow().UtcDateTime.AddDays(-settings.RetentionDays);
        var batchSize = settings.DeleteBatchSize;

        var total = 0;
        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            ct.ThrowIfCancellationRequested();

            var deleted = await _context.NotificationEventLogs
                .Where(r => r.ProcessedAt < cutoff)
                .OrderBy(r => r.ProcessedAt)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);

            total += deleted;
            if (deleted < batchSize)
                break;
        }

        if (total > 0)
        {
            _logger.LogInformation(
                "[NotificationEventLogRetention] {Count} satır silindi (kesim {Cutoff:o}, saklama {Days} gün).",
                total, cutoff, settings.RetentionDays);
        }

        return total;
    }
}

/// <summary><see cref="NotificationEventLogRetentionJob"/>'u periyodik tetikler (PeriodicTimer; Hangfire yok).</summary>
public sealed class NotificationEventLogRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<NotificationEventLogRetentionOptions> _options;
    private readonly ILogger<NotificationEventLogRetentionService> _logger;

    public NotificationEventLogRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<NotificationEventLogRetentionOptions> options,
        ILogger<NotificationEventLogRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.CurrentValue.Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var job = scope.ServiceProvider.GetRequiredService<INotificationEventLogRetentionJob>();
                await job.PurgeExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NotificationEventLogRetention] Temizleme turu başarısız; bir sonraki turda tekrar denenecek.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

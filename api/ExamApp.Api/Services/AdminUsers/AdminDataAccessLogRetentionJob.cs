using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// <c>AdminDataAccessLog</c> ayarları (issue #262, KVKK saklama süresi). Ürün kararı: 6 ay (180 gün).
/// </summary>
public sealed class AdminDataAccessLogOptions
{
    public const string SectionName = "AdminDataAccessLog";

    /// <summary>Bu kadar günden eski audit satırları silinir.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 180;

    /// <summary>Tek DELETE ifadesinin sileceği en fazla satır (uzun kilit/WAL patlamasını önler).</summary>
    [Range(1, 100_000)]
    public int DeleteBatchSize { get; set; } = 5_000;

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: her gün 03:30.</summary>
    [Required]
    public string Cron { get; set; } = "30 3 * * *";
}

/// <summary>issue #262: süresi dolan admin veri erişim kayıtlarını siler (günlük Hangfire recurring job).</summary>
public interface IAdminDataAccessLogRetentionJob
{
    /// <summary>
    /// Silinen toplam satır sayısını döner. Hangfire filtreleri arayüzde: iş <c>AddOrUpdate&lt;IAdminDataAccessLogRetentionJob&gt;</c>
    /// ile kaydedildiği için Hangfire attribute'ları bu metottan okur (bkz. <c>IWorksheetReminderDispatcher</c>).
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}

/// <summary>
/// <see cref="AdminDataAccessLog"/> tablosu yalnız ekleme alır; bu iş saklama süresini (<see cref="AdminDataAccessLogOptions.RetentionDays"/>)
/// aşan satırları <c>ExecuteDelete</c> ile, <see cref="AdminDataAccessLogOptions.DeleteBatchSize"/>'lık partiler halinde siler
/// (entity yüklenmez). Rate limit reddi (<c>Outcome=RateLimited</c>) satırları da aynı kurala tabidir.
/// Kesim anı iş başında bir kez hesaplanır; partiler arasında yeni eklenen satırlar etkilenmez.
/// Aynı anda iki örnek çalışmasın diye arayüzde <see cref="DisableConcurrentExecutionAttribute"/> (çok replica'da tek iş).
/// </summary>
public sealed class AdminDataAccessLogRetentionJob : IAdminDataAccessLogRetentionJob
{
    public const string RecurringJobId = "admin-data-access-log-retention";

    /// <summary>Sonsuz döngü emniyeti: tek çalıştırmada en fazla bu kadar parti (kalan bir sonraki güne kalır).</summary>
    internal const int MaxBatchesPerRun = 1_000;

    private readonly AppDbContext _context;
    private readonly IOptionsMonitor<AdminDataAccessLogOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AdminDataAccessLogRetentionJob> _logger;

    public AdminDataAccessLogRetentionJob(
        AppDbContext context,
        IOptionsMonitor<AdminDataAccessLogOptions> options,
        TimeProvider? clock = null,
        ILogger<AdminDataAccessLogRetentionJob>? logger = null)
    {
        _context = context;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AdminDataAccessLogRetentionJob>.Instance;
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

            // Id sırasıyla parti: DELETE ... WHERE "Id" IN (SELECT "Id" ... ORDER BY "Id" LIMIT n).
            var deleted = await _context.AdminDataAccessLogs
                .Where(r => r.OccurredAtUtc < cutoff)
                .OrderBy(r => r.Id)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);

            total += deleted;
            if (deleted < batchSize)
                break;
        }

        if (total > 0)
        {
            _logger.LogInformation(
                "[AdminDataAccessLogRetention] {Count} audit satırı silindi (kesim {Cutoff:o}, saklama {Days} gün).",
                total, cutoff, settings.RetentionDays);
        }

        return total;
    }
}

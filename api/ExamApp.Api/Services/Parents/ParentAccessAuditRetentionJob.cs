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

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// <c>ParentAccessAudit</c> ayarları (issue #424, epic #407 V6 — KVKK saklama süresi). <c>AdminDataAccessLog</c> (#262) ile aynı
/// karar: 6 ay (180 gün).
/// </summary>
public sealed class ParentAccessAuditOptions
{
    public const string SectionName = "ParentAccessAudit";

    /// <summary>Bu kadar günden eski veli erişim kayıtları silinir.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 180;

    /// <summary>Tek DELETE ifadesinin sileceği en fazla satır (uzun kilit/WAL patlamasını önler).</summary>
    [Range(1, 100_000)]
    public int DeleteBatchSize { get; set; } = 5_000;

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: her gün 03:45 (admin audit temizliğinden 15 dk sonra).</summary>
    [Required]
    public string Cron { get; set; } = "45 3 * * *";
}

/// <summary>issue #424: süresi dolan veli erişim kayıtlarını siler (günlük Hangfire recurring job).</summary>
public interface IParentAccessAuditRetentionJob
{
    /// <summary>
    /// Silinen toplam satır sayısını döner. Hangfire filtreleri arayüzde: iş <c>AddOrUpdate&lt;IParentAccessAuditRetentionJob&gt;</c>
    /// ile kaydedildiği için Hangfire attribute'ları bu metottan okur.
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}

/// <summary>
/// <see cref="ParentAccessAudit"/> tablosu yalnız ekleme alır; bu iş saklama süresini (<see cref="ParentAccessAuditOptions.RetentionDays"/>)
/// aşan satırları <c>ExecuteDelete</c> ile, <see cref="ParentAccessAuditOptions.DeleteBatchSize"/>'lık partiler halinde siler
/// (entity yüklenmez; <c>IX_ParentAccessAudits_At</c>). <c>AdminDataAccessLogRetentionJob</c> (#262) deseni: kesim anı iş başında
/// bir kez hesaplanır, partiler arasında eklenen satırlar etkilenmez; çok replica'da tek örnek çalışır.
/// </summary>
public sealed class ParentAccessAuditRetentionJob : IParentAccessAuditRetentionJob
{
    public const string RecurringJobId = "parent-access-audit-retention";

    /// <summary>Sonsuz döngü emniyeti: tek çalıştırmada en fazla bu kadar parti (kalan bir sonraki güne kalır).</summary>
    internal const int MaxBatchesPerRun = 1_000;

    private readonly AppDbContext _context;
    private readonly IOptionsMonitor<ParentAccessAuditOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ParentAccessAuditRetentionJob> _logger;

    public ParentAccessAuditRetentionJob(
        AppDbContext context,
        IOptionsMonitor<ParentAccessAuditOptions> options,
        TimeProvider? clock = null,
        ILogger<ParentAccessAuditRetentionJob>? logger = null)
    {
        _context = context;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ParentAccessAuditRetentionJob>.Instance;
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
            var deleted = await _context.ParentAccessAudits
                .Where(a => a.At < cutoff)
                .OrderBy(a => a.Id)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);

            total += deleted;
            if (deleted < batchSize)
                break;
        }

        if (total > 0)
        {
            _logger.LogInformation(
                "[ParentAccessAuditRetention] {Count} veli erişim kaydı silindi (kesim {Cutoff:o}, saklama {Days} gün).",
                total, cutoff, settings.RetentionDays);
        }

        return total;
    }
}

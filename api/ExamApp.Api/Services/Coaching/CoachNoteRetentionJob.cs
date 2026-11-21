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

namespace ExamApp.Api.Services.Coaching;

/// <summary>
/// <c>CoachNoteRetention</c> ayarları (issue #810). Koç notu öğretmenin bir çocuk hakkındaki özel notudur; kimsenin
/// dokunmadığı not süresiz tutulmaz. Varsayılan 2 yıl (730 gün) <c>UpdatedAt</c>'ten; süre KVKK metinleriyle (#522)
/// birlikte kesinleşecek, değişirse yalnız ayar değişir.
/// </summary>
public sealed class CoachNoteRetentionOptions
{
    public const string SectionName = "CoachNoteRetention";

    /// <summary>Bu kadar gündür güncellenmeyen koç notları silinir.</summary>
    [Range(30, 3650)]
    public int RetentionDays { get; set; } = 730;

    /// <summary>Tek DELETE ifadesinin sileceği en fazla satır.</summary>
    [Range(1, 100_000)]
    public int DeleteBatchSize { get; set; } = 5_000;

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: her gün 04:15 (moderasyon geçmişi temizliğinden sonra).</summary>
    [Required]
    public string Cron { get; set; } = "15 4 * * *";
}

/// <summary>issue #810: süresi dolan koç notlarını siler (günlük Hangfire recurring job).</summary>
public interface ICoachNoteRetentionJob
{
    /// <summary>Silinen toplam satır sayısını döner. Hangfire filtreleri arayüzde (iş arayüzle kaydedilir).</summary>
    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}

/// <summary>
/// <see cref="CoachNote"/> satırlarını <c>UpdatedAt</c> kesimine göre <c>ExecuteDelete</c> ile partiler halinde, kalıcı olarak siler
/// (notlar soft delete değildir). Hedefler (<c>CoachGoal</c>) etkilenmez. <c>ParentAccessAuditRetentionJob</c> (#424) deseni:
/// kesim anı iş başında bir kez hesaplanır, partiler arasında güncellenen not etkilenmez. Tablo küçük (öğretmen × öğrenci
/// notları); <c>UpdatedAt</c> için ayrı index yok, günlük tek tarama yeterli.
/// </summary>
public sealed class CoachNoteRetentionJob : ICoachNoteRetentionJob
{
    public const string RecurringJobId = "coach-note-retention";

    /// <summary>Sonsuz döngü emniyeti: tek çalıştırmada en fazla bu kadar parti (kalan bir sonraki güne kalır).</summary>
    internal const int MaxBatchesPerRun = 1_000;

    private readonly AppDbContext _context;
    private readonly IOptionsMonitor<CoachNoteRetentionOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<CoachNoteRetentionJob> _logger;

    public CoachNoteRetentionJob(
        AppDbContext context,
        IOptionsMonitor<CoachNoteRetentionOptions> options,
        TimeProvider? clock = null,
        ILogger<CoachNoteRetentionJob>? logger = null)
    {
        _context = context;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CoachNoteRetentionJob>.Instance;
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

            var deleted = await _context.CoachNotes
                .Where(n => n.UpdatedAt < cutoff)
                .OrderBy(n => n.Id)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);

            total += deleted;
            if (deleted < batchSize)
                break;
        }

        if (total > 0)
        {
            _logger.LogInformation(
                "[CoachNoteRetention] {Count} koç notu silindi (kesim {Cutoff:o}, saklama {Days} gün).",
                total, cutoff, settings.RetentionDays);
        }

        return total;
    }
}

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

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// <c>WorksheetCommentModerationEvents</c> saklama ayarları (issue #335, #326 D1). Süreler BA taslağıdır (PO/hukuk onayı bekliyor):
/// gizle/aç 2 yıl, reddedilen deneme 90 gün (yüksek hacim, güvenlik izi).
/// </summary>
public sealed class CommentModerationHistoryOptions
{
    public const string SectionName = "CommentModerationHistory";

    /// <summary>Bu kadar günden eski <c>Hide</c>/<c>Unhide</c> olayları silinir.</summary>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 730;

    /// <summary>Bu kadar günden eski <c>DeniedNotModerator</c> olayları silinir.</summary>
    [Range(1, 3650)]
    public int DeniedRetentionDays { get; set; } = 90;

    /// <summary>Tek DELETE ifadesinin sileceği en fazla satır (uzun kilit/WAL patlamasını önler).</summary>
    [Range(1, 100_000)]
    public int DeleteBatchSize { get; set; } = 5_000;

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: her gün 04:00 (veli erişim kaydı temizliğinden 15 dk sonra).</summary>
    [Required]
    public string Cron { get; set; } = "0 4 * * *";
}

/// <summary>issue #335: süresi dolan yorum moderasyon olaylarını siler (günlük Hangfire recurring job).</summary>
public interface IWorksheetCommentModerationHistoryRetentionJob
{
    /// <summary>
    /// Silinen toplam satır sayısını döner. Hangfire filtreleri arayüzde: iş
    /// <c>AddOrUpdate&lt;IWorksheetCommentModerationHistoryRetentionJob&gt;</c> ile kaydedildiği için attribute'lar buradan okunur.
    /// </summary>
    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}

/// <summary>
/// <see cref="WorksheetCommentModerationEvent"/> tablosu yalnız ekleme alır; bu iş olay türüne göre iki kesimle
/// (<see cref="CommentModerationHistoryOptions.RetentionDays"/> gizle/aç, <see cref="CommentModerationHistoryOptions.DeniedRetentionDays"/>
/// reddedilen deneme) satırları <c>ExecuteDelete</c> ile, <see cref="CommentModerationHistoryOptions.DeleteBatchSize"/>'lık partiler
/// halinde siler (<c>IX (Action, CreatedAt)</c>). <c>ParentAccessAuditRetentionJob</c> (#424) deseni: kesim anı iş başında bir kez
/// hesaplanır, çok replica'da tek örnek çalışır.
/// </summary>
public sealed class WorksheetCommentModerationHistoryRetentionJob : IWorksheetCommentModerationHistoryRetentionJob
{
    public const string RecurringJobId = "comment-moderation-history-retention";

    /// <summary>Sonsuz döngü emniyeti: tür başına tek çalıştırmada en fazla bu kadar parti (kalan bir sonraki güne kalır).</summary>
    internal const int MaxBatchesPerRun = 1_000;

    private readonly AppDbContext _context;
    private readonly IOptionsMonitor<CommentModerationHistoryOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<WorksheetCommentModerationHistoryRetentionJob> _logger;

    public WorksheetCommentModerationHistoryRetentionJob(
        AppDbContext context,
        IOptionsMonitor<CommentModerationHistoryOptions> options,
        TimeProvider? clock = null,
        ILogger<WorksheetCommentModerationHistoryRetentionJob>? logger = null)
    {
        _context = context;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<WorksheetCommentModerationHistoryRetentionJob>.Instance;
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var settings = _options.CurrentValue;
        var now = _clock.GetUtcNow().UtcDateTime;
        var moderationCutoff = now.AddDays(-settings.RetentionDays);
        var deniedCutoff = now.AddDays(-settings.DeniedRetentionDays);

        var moderation = await PurgeAsync(
            m => (m.Action == WorksheetCommentModerationAction.Hide || m.Action == WorksheetCommentModerationAction.Unhide)
                && m.CreatedAt < moderationCutoff,
            settings.DeleteBatchSize, ct);
        var denied = await PurgeAsync(
            m => m.Action == WorksheetCommentModerationAction.DeniedNotModerator && m.CreatedAt < deniedCutoff,
            settings.DeleteBatchSize, ct);

        if (moderation + denied > 0)
        {
            _logger.LogInformation(
                "[CommentModerationHistoryRetention] {Moderation} gizle/aç (kesim {ModerationCutoff:o}) ve {Denied} reddedilen deneme (kesim {DeniedCutoff:o}) olayı silindi.",
                moderation, moderationCutoff, denied, deniedCutoff);
        }

        return moderation + denied;
    }

    private async Task<int> PurgeAsync(
        System.Linq.Expressions.Expression<Func<WorksheetCommentModerationEvent, bool>> expired, int batchSize, CancellationToken ct)
    {
        var total = 0;
        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            ct.ThrowIfCancellationRequested();

            // Id sırasıyla parti: DELETE ... WHERE "Id" IN (SELECT "Id" ... ORDER BY "Id" LIMIT n).
            var deleted = await _context.WorksheetCommentModerationEvents
                .Where(expired)
                .OrderBy(m => m.Id)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);

            total += deleted;
            if (deleted < batchSize)
                break;
        }

        return total;
    }
}

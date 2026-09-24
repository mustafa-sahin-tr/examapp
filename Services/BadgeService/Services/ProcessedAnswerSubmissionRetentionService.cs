using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BadgeService.Services;

/// <summary>
/// issue #279 (item 3): <c>ProcessedAnswerSubmissions</c> yalnız ekleme alan bir idempotency defteri —
/// süresiz büyür. Bu iş <see cref="ProcessedAnswerSubmissionRetentionOptions.RetentionDays"/>'i (varsayılan
/// 30 gün) aşan satırları <c>ExecuteDelete</c> ile, <see cref="ProcessedAnswerSubmissionRetentionOptions.DeleteBatchSize"/>'lık
/// partiler halinde siler (<c>AdminDataAccessLogRetentionJob</c> ile aynı desen — entity yüklenmez).
///
/// Yalnızca ledger'ı temizler; <c>AnswerPointAward</c> BİLEREK silinmez (30 günden eski bir sınav bile
/// tekrar cevaplanabilirse — ör. öğretmen worksheet'i yeniden açarsa — o satır hâlâ "son uygulanan puan"ı
/// tutmalı; silinirse bir sonraki mesaj puanı sıfırdan (delta = tam puan) uygular ve KATLANMIŞ puan riski
/// geri döner). <c>AnswerPointAward</c> yalnızca öğrenci reset/KVKK akışında (bkz. <c>UserResetService</c>)
/// kullanıcı bazında silinir.
/// </summary>
public interface IProcessedAnswerSubmissionRetentionJob
{
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}

public sealed class ProcessedAnswerSubmissionRetentionJob : IProcessedAnswerSubmissionRetentionJob
{
    /// <summary>Sonsuz döngü emniyeti: tek çalıştırmada en fazla bu kadar parti (kalan bir sonraki tur'a kalır).</summary>
    internal const int MaxBatchesPerRun = 1_000;

    private readonly BadgeDbContext _context;
    private readonly IOptionsMonitor<ProcessedAnswerSubmissionRetentionOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProcessedAnswerSubmissionRetentionJob> _logger;

    public ProcessedAnswerSubmissionRetentionJob(
        BadgeDbContext context,
        IOptionsMonitor<ProcessedAnswerSubmissionRetentionOptions> options,
        TimeProvider? clock = null,
        ILogger<ProcessedAnswerSubmissionRetentionJob>? logger = null)
    {
        _context = context;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ProcessedAnswerSubmissionRetentionJob>.Instance;
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

            var deleted = await _context.ProcessedAnswerSubmissions
                .Where(r => r.ProcessedAt < cutoff)
                .OrderBy(r => r.EventId)
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);

            total += deleted;
            if (deleted < batchSize)
                break;
        }

        if (total > 0)
        {
            _logger.LogInformation(
                "[ProcessedAnswerSubmissionRetention] {Count} ledger satırı silindi (kesim {Cutoff:o}, saklama {Days} gün).",
                total, cutoff, settings.RetentionDays);
        }

        return total;
    }
}

/// <summary>
/// <see cref="ProcessedAnswerSubmissionRetentionJob"/>'u periyodik olarak tetikleyen arka plan hizmeti.
/// BadgeService'te Hangfire yok; yeni bir job scheduler bağımlılığı eklemek yerine <see cref="PeriodicTimer"/>
/// tabanlı hafif bir döngü kullanılır (issue #279 item 3 — rapor: "seçim" notu). Çoklu instance'ta her
/// replica kendi zamanlayıcısını çalıştırır; iş idempotent (ExecuteDelete, kesim koşullu) olduğundan aynı
/// anda birden fazla replica'nın tetiklemesi zararsızdır (en kötü ihtimalle fazladan no-op DELETE).
/// </summary>
public sealed class ProcessedAnswerSubmissionRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ProcessedAnswerSubmissionRetentionOptions> _options;
    private readonly ILogger<ProcessedAnswerSubmissionRetentionService> _logger;

    public ProcessedAnswerSubmissionRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ProcessedAnswerSubmissionRetentionOptions> options,
        ILogger<ProcessedAnswerSubmissionRetentionService> logger)
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
                var job = scope.ServiceProvider.GetRequiredService<IProcessedAnswerSubmissionRetentionJob>();
                await job.PurgeExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Servis kapanıyor — normal.
            }
            catch (Exception ex)
            {
                // Best-effort: bir turun hatası servisi durdurmaz, bir sonraki turda tekrar denenir.
                _logger.LogError(ex, "[ProcessedAnswerSubmissionRetention] Temizleme turu başarısız; bir sonraki turda tekrar denenecek.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

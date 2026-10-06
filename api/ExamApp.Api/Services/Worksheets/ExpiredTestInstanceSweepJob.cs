using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>issue #396: süresi dolmuş açık test oturumu süpürücüsünün ayarları.</summary>
public sealed class ExpiredTestInstanceSweepOptions
{
    public const string SectionName = "ExpiredTestInstanceSweep";

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: 5 dakikada bir.</summary>
    [Required]
    public string Cron { get; set; } = "*/5 * * * *";

    /// <summary>Tek turda en fazla işlenecek oturum (kalan bir sonraki tura kalır).</summary>
    [Range(1, MaxBatchSize)]
    public int BatchSize { get; set; } = 500;

    public const int MaxBatchSize = 5_000;
}

/// <summary>issue #396: süresi (+ tolerans) dolmuş Started oturumları Expired'a çeken Hangfire recurring job.</summary>
public interface IExpiredTestInstanceSweepJob
{
    /// <summary>
    /// Expired'a çekilen oturum sayısını döner. Retry yok: bir sonraki cron turu zaten yeniden dener. Filtreler arayüzde
    /// (iş <c>AddOrUpdate&lt;IExpiredTestInstanceSweepJob&gt;</c> ile kaydedilir).
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> SweepAsync(CancellationToken ct = default);
}

/// <summary>
/// issue #396: öğrenci sekmeyi kapatıp hiç dönmezse oturum istek anında (save/end/start) Expired'a çekilemez ve Started
/// kalır — öğretmen raporları "devam ediyor" gösterir. Bu iş onları kapatır. Kural <see cref="TestTimeLimit"/> ile aynıdır
/// (oturumun kopyalanmış süre sınırı + tolerans). Yazma koşulludur (<c>WHERE Status = Started</c>): aynı anda tamamlanan ya
/// da istek yolunda Expired'a çekilen oturuma dokunulmaz, EndTime/UpdateTime ilk yazanın kalır. UpdateUserId null (sistem).
/// Aday seçimi <c>IX_TestInstances_StartTime_Started</c> filtreli index'ini kullanır.
/// </summary>
public sealed class ExpiredTestInstanceSweepJob : IExpiredTestInstanceSweepJob
{
    public const string RecurringJobId = "expired-test-instance-sweep";

    private readonly AppDbContext _context;
    private readonly IOptionsMonitor<ExpiredTestInstanceSweepOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExpiredTestInstanceSweepJob> _logger;

    public ExpiredTestInstanceSweepJob(
        AppDbContext context,
        IOptionsMonitor<ExpiredTestInstanceSweepOptions> options,
        TimeProvider? clock = null,
        ILogger<ExpiredTestInstanceSweepJob>? logger = null)
    {
        _context = context;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ExpiredTestInstanceSweepJob>.Instance;
    }

    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        // StartTime + limit + tolerans < now  ⇔  StartTime + limit < now - tolerans.
        var cutoff = now - TestTimeLimit.Tolerance;

        var ids = await _context.TestInstances
            .AsNoTracking()
            .Where(ti => ti.Status == WorksheetInstanceStatus.Started
                && ti.MaxDurationSeconds > 0
                && ti.StartTime < cutoff
                && ti.StartTime.AddSeconds((double)ti.MaxDurationSeconds!.Value) < cutoff)
            .OrderBy(ti => ti.StartTime)
            .Take(_options.CurrentValue.BatchSize)
            .Select(ti => ti.Id)
            .ToListAsync(ct);

        if (ids.Count == 0)
            return 0;

        // Tek set-tabanlı UPDATE. "Status = Started" koşulu: okuma ile yazma arasında tamamlanan ya da istek yolunda
        // Expired'a çekilen oturuma dokunulmaz. EndTime satır başına StartTime + kendi süre sınırı (sunucuda hesaplanır).
        var expired = await _context.TestInstances
            .Where(ti => ids.Contains(ti.Id) && ti.Status == WorksheetInstanceStatus.Started)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ti => ti.Status, WorksheetInstanceStatus.Expired)
                .SetProperty(ti => ti.EndTime, ti => (DateTime?)ti.StartTime.AddSeconds((double)ti.MaxDurationSeconds!.Value))
                .SetProperty(ti => ti.UpdateTime, (DateTime?)now)
                .SetProperty(ti => ti.UpdateUserId, (int?)null), ct);

        if (expired > 0)
        {
            _logger.LogInformation("[ExpiredTestInstanceSweep] {Count} süresi dolmuş test oturumu Expired yapıldı.", expired);
        }

        return expired;
    }
}

using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Parents;

/// <summary>issue #436: veli bağlantısı geçiş/bakım süpürücüsünün ayarları.</summary>
public sealed class ParentLinkTransitionSweepOptions
{
    public const string SectionName = "ParentLinkTransitionSweep";

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: saatte bir (dakika 23).</summary>
    [Required]
    public string Cron { get; set; } = "23 * * * *";

    /// <summary>Tek turda birincilliği onarılacak en fazla öğrenci; kalanı sonraki tura kalır.</summary>
    [Range(1, MaxBatchSize)]
    public int BatchSize { get; set; } = 200;

    public const int MaxBatchSize = 2_000;
}

/// <summary>issue #436: süresi dolan bekleyen veli isteklerini kapatan + birincil veliyi onaran Hangfire recurring job.</summary>
public interface IParentLinkTransitionSweepJob
{
    /// <summary>Kapatılan Pending + birincilliği onarılan öğrenci sayısını döner. Retry yok: sonraki cron turu zaten yeniden dener.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<ParentLinkTransitionSweepResult> SweepAsync(CancellationToken ct = default);
}

/// <summary>Bir süpürme turunun sonucu.</summary>
public sealed record ParentLinkTransitionSweepResult(int ExpiredPendingRevoked, int PrimaryRepaired);

/// <summary>
/// issue #436 (epic #435): veli-öncelikli modele geçiş ve bakım.
/// <list type="number">
/// <item><b>Geçiş temizliği:</b> #419'dan kalan (LegacyV1) Pending istekler oluşturulmadan 30 gün sonra, yeni (ikinci veli)
/// istekler 7 gün sonra Revoked'a çekilir (<c>RevokedByUserId</c> null = süre doldu). Sorgular zaten süresi dolanı yok sayar;
/// job satırı kalıcı kapatır ki öğrenci onay yolu kesin olarak kapansın. Pending hiç erişim vermediği için event yazılmaz.</item>
/// <item><b>Birincil devri:</b> birincil velinin hesabı silinince (Parents soft-delete) bunu yakalayan bir yazım yolu yok; job
/// Active velisi olup geçerli birincili olmayan öğrencilerde kalan en eski Active veliyi birincil yapar
/// (<see cref="ParentLinkPrimary.EnsurePrimaryAsync"/>, öğrenci kilidi altında). Okuma uçları aynı kuralı anında uygular; job
/// yalnız kalıcı işareti düzeltir.</item>
/// </list>
/// </summary>
public sealed class ParentLinkTransitionSweepJob : IParentLinkTransitionSweepJob
{
    public const string RecurringJobId = "parent-link-transition-sweep";

    private readonly AppDbContext _context;
    private readonly IOptionsMonitor<ParentLinkTransitionSweepOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ParentLinkTransitionSweepJob> _logger;

    public ParentLinkTransitionSweepJob(
        AppDbContext context,
        IOptionsMonitor<ParentLinkTransitionSweepOptions> options,
        TimeProvider? clock = null,
        ILogger<ParentLinkTransitionSweepJob>? logger = null)
    {
        _context = context;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ParentLinkTransitionSweepJob>.Instance;
    }

    public async Task<ParentLinkTransitionSweepResult> SweepAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        // 1) Süresi dolmuş Pending → Revoked (koşullu UPDATE: arada onaylanan satıra dokunmaz).
        var revoked = await _context.ParentStudentLinks
            .Where(ParentLinkPrimary.IsExpiredPending(now))
            .ExecuteUpdateAsync(set => set
                .SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                .SetProperty(l => l.RevokedAt, now), ct);

        // 2) Active velisi olan ama geçerli (velisi silinmemiş, işaretli) birincili olmayan öğrenciler.
        var batch = _options.CurrentValue.BatchSize;
        var studentIds = await _context.ParentStudentLinks.AsNoTracking()
            .Where(ParentLinkPrimary.IsLiveActive)
            .Where(l => !_context.ParentStudentLinks.Any(p => p.StudentId == l.StudentId && p.IsPrimary
                && p.Status == ParentStudentLinkStatus.Active && !p.Parent.IsDeleted))
            .Select(l => l.StudentId)
            .Distinct()
            .OrderBy(id => id)
            .Take(batch)
            .ToListAsync(ct);

        var repaired = 0;
        foreach (var studentId in studentIds)
        {
            try
            {
                var changed = 0;
                await ParentLinkPrimary.InStudentLockAsync(_context, studentId, async token =>
                {
                    changed = (await ParentLinkPrimary.EnsurePrimaryAsync(_context, studentId, token)).Changed;
                    return true;
                }, ct);
                // Yalnız gerçekten işaret değiştiyse sayılır (aday seçimi ile kilit arasında başka yazım onarmış olabilir).
                if (changed > 0)
                    repaired++;
            }
            catch (ParentLinkLockTimeoutException ex)
            {
                // Eşzamanlı bir bağlantı yazımı kilidi tutuyor; o yazım zaten birincili onarır, yoksa sonraki tur.
                _logger.LogWarning(ex, "[ParentLinkSweep] Öğrenci kilidi alınamadı, atlandı: studentId={StudentId}", studentId);
            }
        }

        if (studentIds.Count == batch)
            _logger.LogWarning("[ParentLinkSweep] Birincil onarım tavanına ulaşıldı ({Batch}); kalan öğrenciler sonraki turda.", batch);
        if (revoked > 0 || repaired > 0)
            _logger.LogInformation("[ParentLinkSweep] Süresi dolan bekleyen istek kapatıldı: {Revoked}; birincil onarılan öğrenci: {Repaired}",
                revoked, repaired);

        return new ParentLinkTransitionSweepResult(revoked, repaired);
    }
}

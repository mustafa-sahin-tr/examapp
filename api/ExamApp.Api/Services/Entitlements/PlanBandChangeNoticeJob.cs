using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.TargetGoals;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>Issue #1110: "yenilemede yeni sınıfın fiyatı" uyarısının ayarları.</summary>
public sealed class PlanBandChangeNoticeOptions
{
    public const string SectionName = "PlanBandChangeNotice";

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: saat başı 30. dakika.</summary>
    [Required]
    public string Cron { get; set; } = "30 * * * *";

    /// <summary>Bitişine bu kadar gün (ya da daha az) kalan plan için velisine uyarılır.</summary>
    [Range(1, 60)]
    public int LeadDays { get; set; } = 30;

    /// <summary>Tek turda işlenecek en fazla atama; kalanı sonraki tura kalır.</summary>
    [Range(1, 2_000)]
    public int BatchSize { get; set; } = 200;
}

/// <summary>Issue #1110: sınıf bandı değişen öğrencinin planı bitmeden velilerine <see cref="ParentChildPlanEndingEvent"/> yazan job.</summary>
public interface IPlanBandChangeNoticeJob
{
    /// <summary>İşlenen (işaretlenen) atama sayısını döner. Retry yok: sonraki tur zaten yeniden dener.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #1110: 1 Ağustos sınıf atlaması ödenmiş dönemin fiyatını değiştirmez; yeni bandın (4→5, 7→8, 8→9, 11→12) fiyatı yenilemede
/// geçerli olur (teklif öğrencinin o anki sınıfından). Velisi bunu bitişten <see cref="PlanBandChangeNoticeOptions.LeadDays"/> gün
/// önce öğrenir. Kurallar:
/// <list type="bullet">
/// <item>Aday: <see cref="ParentPlanEndingReminderJob"/> ile aynı (silinmemiş, bitişi pencerede, ardından başlayan atama yok, Free değil,
/// en az bir Active veli); deneme hariç (ilk satın alma zaten güncel sınıftan). Ek olarak atama başladıktan sonra geri alınmamış bir
/// sınıf atlaması var.</item>
/// <item>Satın almadaki sınıf = atamadan sonraki ilk atlamanın <see cref="StudentGradePromotion.FromGradeId"/>'si; bandı öğrencinin
/// bugünkü sınıfının bandından farklıysa uyarı (<see cref="ParentChildPlanEndingEvent.NewGradeLevel"/> dolu). Band aynıysa, okul
/// lisansı bitişten sonra da sürüyorsa ya da sınıf okunamıyorsa yalnız işaretlenir.</item>
/// <item>Atama başına bir kez: <see cref="UserSubscriptionPlan.BandChangeNoticeSentAtUtc"/> event'lerle AYNI transaction'da; Active
/// veli listesi transaction içinde <c>FOR SHARE</c> ile yeniden okunur (#423).</item>
/// </list>
/// </summary>
public sealed class PlanBandChangeNoticeJob : IPlanBandChangeNoticeJob
{
    public const string RecurringJobId = "plan-band-change-notice";

    private readonly AppDbContext _context;
    private readonly IAuthApiClient? _authApi;
    private readonly IOptionsMonitor<PlanBandChangeNoticeOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<PlanBandChangeNoticeJob> _logger;

    public PlanBandChangeNoticeJob(
        AppDbContext context,
        IAuthApiClient? authApi,
        IOptionsMonitor<PlanBandChangeNoticeOptions> options,
        TimeProvider? clock = null,
        ILogger<PlanBandChangeNoticeJob>? logger = null)
    {
        _context = context;
        _authApi = authApi;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<PlanBandChangeNoticeJob>.Instance;
    }

    private sealed record Candidate(int AssignmentId, int StudentId, int StudentUserId, int? GradeId, DateTime ValidFrom, DateTime EndsAt);

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var now = _clock.GetUtcNow().UtcDateTime;
        var until = now.AddDays(opts.LeadDays);

        var activeLinks = _context.ParentStudentLinks.Where(ParentChildAccess.GrantsAccess);
        var candidates = await (
            from a in _context.UserSubscriptionPlans
            where a.BandChangeNoticeSentAtUtc == null && a.ValidToUtc != null && a.ValidToUtc > now && a.ValidToUtc <= until
                && a.Plan.Key != SubscriptionPlan.FreeKey && a.Plan.TrialDays == null
                && (a.Plan.Audience == SubscriptionPlanAudience.Student || a.Plan.Audience == SubscriptionPlanAudience.Any)
                && !_context.UserSubscriptionPlans.Any(next => next.UserId == a.UserId && next.Id != a.Id && next.ValidFromUtc >= a.ValidToUtc)
            from s in _context.Students
            where s.UserId == a.UserId && !s.IsDeleted
                && activeLinks.Any(l => l.StudentId == s.Id)
                && _context.StudentGradePromotions.Any(p => p.StudentId == s.Id && p.RevertedAtUtc == null && p.PromotedAtUtc >= a.ValidFromUtc)
            orderby a.ValidToUtc, a.Id
            select new Candidate(a.Id, s.Id, s.UserId, s.GradeId, a.ValidFromUtc, a.ValidToUtc!.Value))
            .Take(opts.BatchSize)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return 0;

        var studentIds = candidates.Select(c => c.StudentId).Distinct().ToList();
        var promotions = await _context.StudentGradePromotions.AsNoTracking()
            .Where(p => studentIds.Contains(p.StudentId) && p.RevertedAtUtc == null)
            .Select(p => new { p.StudentId, p.FromGradeId, p.PromotedAtUtc })
            .ToListAsync(ct);
        var levels = (await _context.Grades.AsNoTracking().Select(g => new { g.Id, g.Code, g.Name }).ToListAsync(ct))
            .ToDictionary(g => g.Id, g => TargetGoalService.GradeLevelOf(g.Code) ?? TargetGoalService.GradeLevelOf(g.Name));
        int? LevelOf(int? gradeId) => gradeId is { } id && levels.TryGetValue(id, out var l) ? l : null;

        // Satın almadaki sınıfın bandından farklı bir banda geçen öğrencinin yeni sınıf seviyesi; değişmediyse null.
        int? NewLevelOf(Candidate c)
        {
            var first = promotions
                .Where(p => p.StudentId == c.StudentId && p.PromotedAtUtc >= c.ValidFrom)
                .MinBy(p => p.PromotedAtUtc);
            var level = LevelOf(c.GradeId);
            var band = StudentPlanPricing.BandOf(level);
            return first != null && band != null && band != StudentPlanPricing.BandOf(LevelOf(first.FromGradeId)) ? level : null;
        }

        var links = await activeLinks.AsNoTracking()
            .Where(l => studentIds.Contains(l.StudentId))
            .Select(l => new { l.StudentId, l.ParentId, ParentUserId = l.Parent.UserId })
            .ToListAsync(ct);
        var parentsByStudent = links.ToLookup(l => l.StudentId);
        // Issue #1092: okul lisansı atamanın bitişinden sonra da sürüyorsa yenileme yok: uyarı yok, atama işaretlenir.
        var licenses = await SchoolLicensePlans.ForUsersAsync(_context, candidates.Select(c => c.StudentUserId).ToList(), now, ct);
        bool CoveredByLicense(Candidate c) =>
            licenses.TryGetValue(c.StudentUserId, out var l) && (l.Plan.ValidToUtc is null || l.Plan.ValidToUtc >= c.EndsAt);
        var users = await ParentNotificationSupport.LookupAsync(
            _authApi, links.Select(l => l.ParentUserId).Concat(candidates.Select(c => c.StudentUserId)), _logger, ct);

        var processed = 0;
        var notified = 0;
        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var newLevel = CoveredByLicense(c) ? null : NewLevelOf(c);
            var student = ParentNotificationSupport.Of(users, c.StudentUserId);
            try
            {
                var written = 0;
                var done = false;
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    _context.ChangeTracker.Clear();
                    written = 0;
                    done = false;
                    await using var tx = await _context.Database.BeginTransactionAsync(ct);

                    var stillActive = newLevel is null
                        ? []
                        : await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, c.StudentId, ct);
                    var recipients = parentsByStudent[c.StudentId].Where(p => stillActive.Contains(p.ParentId)).ToList();

                    // Koşullu işaret: eşzamanlı ikinci örnek 0 satır günceller ve event yazmaz.
                    var marked = await _context.UserSubscriptionPlans
                        .Where(a => a.Id == c.AssignmentId && a.BandChangeNoticeSentAtUtc == null)
                        .ExecuteUpdateAsync(u => u.SetProperty(a => a.BandChangeNoticeSentAtUtc, now), ct);
                    if (marked == 0)
                    {
                        await tx.RollbackAsync(ct);
                        return;
                    }

                    foreach (var p in recipients)
                    {
                        _context.OutboxMessages.Add(new OutboxMessage
                        {
                            Type = OutboxEventRegistry.NameFor<ParentChildPlanEndingEvent>(),
                            Content = JsonSerializer.Serialize(new ParentChildPlanEndingEvent
                            {
                                EventId = Guid.NewGuid(),
                                AssignmentId = c.AssignmentId,
                                NewGradeLevel = newLevel,
                                EndsAtUtc = DateTime.SpecifyKind(c.EndsAt, DateTimeKind.Utc),
                                StudentId = c.StudentId,
                                StudentDisplayName = student.DisplayName,
                                ParentId = p.ParentId,
                                ParentUserId = p.ParentUserId,
                                ParentKeycloakId = ParentNotificationSupport.Of(users, p.ParentUserId).KeycloakId,
                            }),
                            CreatedAt = now
                        });
                    }
                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    written = recipients.Count;
                    done = true;
                });
                if (done)
                    processed++;
                notified += written;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Tek atamanın hatası turun geri kalanını engellemez; işaret yazılmadığından sonraki turda yeniden denenir.
                _logger.LogError(ex, "[PlanBandChangeNotice] AssignmentId={AssignmentId} işlenemedi; sonraki tura kalır.", c.AssignmentId);
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }

        if (processed > 0)
            _logger.LogInformation("[PlanBandChangeNotice] {Processed} atama işlendi; {Notified} veli event'i yazıldı.", processed, notified);
        return processed;
    }
}

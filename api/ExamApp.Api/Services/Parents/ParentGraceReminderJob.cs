using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Parents;

/// <summary>Issue #441: "veli ekleme süren bitiyor" hatırlatma job'ı.</summary>
public interface IParentGraceReminderJob
{
    /// <summary>Yazılan hatırlatma sayısı. Retry yok: sonraki gün yeniden dener (işaretçi tekilliği sayesinde tekrar bildirim yok).</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #441: hoşgörü süresindeki (velisiz, aktivasyon bekleyen daveti olmayan, sayacı başlamış) öğrencilere bitişten
/// <see cref="ParentRequirementOptions.FinalWarningDays"/> (7) ve 1 gün önce <see cref="StudentParentGraceReminderEvent"/> (BadgeService →
/// uygulama içi bildirim; e-posta değil). (öğrenci, bitiş anı, kalan gün) başına en fazla bir kez: <see cref="StudentParentGraceReminder"/>
/// tekil index'i event'le AYNI transaction'da yazılır. Job bir gün kaçırırsa yalnız EN YAKIN eşik gönderilir (1 gün kala 7 günlük
/// hatırlatma gelmez). Süre bitmiş öğrenci taranmaz.
/// </summary>
public sealed class ParentGraceReminderJob : IParentGraceReminderJob
{
    public const string RecurringJobId = "parent-grace-reminder";

    private readonly AppDbContext _context;
    private readonly IAuthApiClient? _authApi;
    private readonly IOptionsMonitor<ParentRequirementOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ParentGraceReminderJob> _logger;

    public ParentGraceReminderJob(
        AppDbContext context,
        IAuthApiClient? authApi,
        IOptionsMonitor<ParentRequirementOptions> options,
        TimeProvider? clock = null,
        ILogger<ParentGraceReminderJob>? logger = null)
    {
        _context = context;
        _authApi = authApi;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ParentGraceReminderJob>.Instance;
    }

    /// <summary>Kalan süreye göre eşik: ≤ 1 gün → 1, ≤ FinalWarningDays → FinalWarningDays, aksi null.</summary>
    public static int? ThresholdFor(TimeSpan remaining, int finalWarningDays)
    {
        if (remaining <= TimeSpan.Zero)
            return null;
        if (remaining <= TimeSpan.FromDays(1))
            return 1;
        return remaining <= TimeSpan.FromDays(finalWarningDays) ? finalWarningDays : null;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var now = _clock.GetUtcNow().UtcDateTime;
        // StartedAt + GraceDays ∈ (now, now + FinalWarningDays]  ⇔  StartedAt ∈ (now - Grace, now - Grace + FinalWarningDays].
        var lower = now - opts.GracePeriod;
        var upper = lower + TimeSpan.FromDays(opts.FinalWarningDays);

        var candidates = await _context.Students.AsNoTracking()
            .Where(s => s.ParentRequirementStartedAt != null && s.ParentRequirementStartedAt > lower && s.ParentRequirementStartedAt <= upper)
            // Review MINOR8: "Active veli" yaptırımla aynı tanım (ParentChildAccess.GrantsAccess).
            .Where(s => !_context.ParentStudentLinks.Where(l => l.StudentId == s.Id).Where(ParentChildAccess.GrantsAccess).Any())
            .OrderBy(s => s.ParentRequirementStartedAt).ThenBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.UserId,
                StartedAt = s.ParentRequirementStartedAt!.Value,
                OpenInvitationExpiresAt = _context.ParentInvitations.Where(i => i.StudentId == s.Id)
                    .Where(StudentParentStatusService.IsOpenInvitation).Max(i => (DateTime?)i.ExpiresAt)
            })
            .Take(opts.ReminderBatchSize)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return 0;
        if (candidates.Count >= opts.ReminderBatchSize)
            _logger.LogWarning("[ParentGraceReminder] Tur tavanına ulaşıldı ({Batch}); kalanı sonraki tura kalır.", opts.ReminderBatchSize);

        var plans = candidates
            .Select(c =>
            {
                var graceEndsAt = DateTime.SpecifyKind(c.StartedAt + opts.GracePeriod, DateTimeKind.Utc);
                return new { c.Id, c.UserId, GraceEndsAt = graceEndsAt, c.OpenInvitationExpiresAt, Threshold = ThresholdFor(graceEndsAt - now, opts.FinalWarningDays) };
            })
            // Review R3: açık davet ancak süre bitiminden SONRA da geçerliyse (kilit gelmez) hatırlatma gereksiz; davet bitimden önce
            // doluyorsa öğrenci yine kilitlenebilir → hatırlatılır.
            .Where(p => p.Threshold != null
                        && !(p.OpenInvitationExpiresAt is DateTime exp && DateTime.SpecifyKind(exp, DateTimeKind.Utc) > p.GraceEndsAt))
            .ToList();
        if (plans.Count == 0)
            return 0;

        var studentIds = plans.Select(p => p.Id).ToList();
        var sent = await _context.StudentParentGraceReminders.AsNoTracking()
            .Where(r => studentIds.Contains(r.StudentId))
            .Select(r => new { r.StudentId, r.GraceEndsAt, r.DaysLeft })
            .ToListAsync(ct);
        var sentKeys = sent.Select(r => (r.StudentId, DateTime.SpecifyKind(r.GraceEndsAt, DateTimeKind.Utc), r.DaysLeft)).ToHashSet();
        var due = plans.Where(p => !sentKeys.Contains((p.Id, p.GraceEndsAt, p.Threshold!.Value))).ToList();
        if (due.Count == 0)
            return 0;

        var users = await ParentNotificationSupport.LookupAsync(_authApi, due.Select(d => d.UserId), _logger, ct);

        var written = 0;
        foreach (var plan in due)
        {
            // Review MINOR5: bildirim hedefi (sub) çözülemediyse bu tur atlanır — işaretçi YAZILMAZ, sonraki tur yeniden dener (boş hedefli
            // event hatırlatmayı kalıcı olarak "gönderildi" saydırırdı).
            var target = ParentNotificationSupport.Of(users, plan.UserId).KeycloakId;
            if (string.IsNullOrEmpty(target))
            {
                _logger.LogWarning("[ParentGraceReminder] Öğrencinin bildirim hedefi çözülemedi, sonraki tura bırakıldı: studentId={StudentId}", plan.Id);
                continue;
            }

            try
            {
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    _context.ChangeTracker.Clear();
                    await using var tx = await _context.Database.BeginTransactionAsync(ct);
                    _context.StudentParentGraceReminders.Add(new StudentParentGraceReminder
                    {
                        StudentId = plan.Id,
                        GraceEndsAt = plan.GraceEndsAt,
                        DaysLeft = plan.Threshold!.Value,
                        SentAt = now
                    });
                    _context.OutboxMessages.Add(new OutboxMessage
                    {
                        Type = OutboxEventRegistry.NameFor<StudentParentGraceReminderEvent>(),
                        Content = JsonSerializer.Serialize(new StudentParentGraceReminderEvent
                        {
                            StudentId = plan.Id,
                            StudentUserId = target,
                            DaysLeft = plan.Threshold.Value,
                            GraceEndsAt = plan.GraceEndsAt,
                            OccurredAt = now
                        }),
                        CreatedAt = now
                    });
                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                });
                written++;
            }
            catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
            {
                // Eşzamanlı ikinci örnek aynı hatırlatmayı yazdı — tekrar bildirim yok.
                _logger.LogInformation("[ParentGraceReminder] Hatırlatma zaten yazılmış: studentId={StudentId}", plan.Id);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "[ParentGraceReminder] Hatırlatma yazılamadı: studentId={StudentId}", plan.Id);
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }

        if (written > 0)
            _logger.LogInformation("[ParentGraceReminder] Veli ekleme süresi hatırlatması yazıldı: {Count}", written);
        return written;
    }
}

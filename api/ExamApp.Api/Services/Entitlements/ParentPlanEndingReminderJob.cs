using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>Issue #1091: "çocuğun planı bitiyor" hatırlatmasının ayarları.</summary>
public sealed class ParentPlanEndingReminderOptions
{
    public const string SectionName = "ParentPlanEndingReminder";

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: saat başı.</summary>
    [Required]
    public string Cron { get; set; } = "0 * * * *";

    /// <summary>Bitişine bu kadar gün (ya da daha az) kalan plan / deneme için velisine hatırlatılır.</summary>
    [Range(1, 30)]
    public int LeadDays { get; set; } = 3;

    /// <summary>Tek turda işlenecek en fazla atama; kalanı sonraki tura kalır.</summary>
    [Range(1, 2_000)]
    public int BatchSize { get; set; } = 200;
}

/// <summary>Issue #1091: bitişi yaklaşan öğrenci planları için velilere <see cref="ParentChildPlanEndingEvent"/> yazan Hangfire job.</summary>
public interface IParentPlanEndingReminderJob
{
    /// <summary>İşlenen (işaretlenen) atama sayısını döner. Retry yok: sonraki tur zaten yeniden dener.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #1091: öğrencinin planı ya da denemesi <see cref="ParentPlanEndingReminderOptions.LeadDays"/> gün içinde bitiyorsa her
/// Active velisine bir bildirim event'i. Kurallar:
/// <list type="bullet">
/// <item>Aday: silinmemiş, bitişi gelecekte ve pencere içinde, henüz hatırlatılmamış atama; kullanıcı silinmemiş bir öğrenci ve en az
/// bir Active velisi var (velisi olmayan öğrenci işaretlenmez: sonradan bağlanan veli hatırlatmayı yine alır). Ardından başlayan
/// bir atama (önceden yenilenmiş plan) varsa hatırlatılmaz. Free hiç bitmez. Okul lisansı (#1092) atamadan sonra da sürüyorsa
/// hatırlatılmaz, yalnız işaretlenir.</item>
/// <item>Atama başına bir kez: <see cref="UserSubscriptionPlan.EndingReminderSentAtUtc"/> event'lerle AYNI transaction'da yazılır;
/// Active veli listesi transaction içinde <c>FOR SHARE</c> ile yeniden okunur (koparma yarışı, #423 ile aynı).</item>
/// </list>
/// </summary>
public sealed class ParentPlanEndingReminderJob : IParentPlanEndingReminderJob
{
    public const string RecurringJobId = "parent-plan-ending-reminder";

    private readonly AppDbContext _context;
    private readonly IAuthApiClient? _authApi;
    private readonly IOptionsMonitor<ParentPlanEndingReminderOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ParentPlanEndingReminderJob> _logger;

    public ParentPlanEndingReminderJob(
        AppDbContext context,
        IAuthApiClient? authApi,
        IOptionsMonitor<ParentPlanEndingReminderOptions> options,
        TimeProvider? clock = null,
        ILogger<ParentPlanEndingReminderJob>? logger = null)
    {
        _context = context;
        _authApi = authApi;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ParentPlanEndingReminderJob>.Instance;
    }

    private sealed record Candidate(int AssignmentId, int StudentId, int StudentUserId, DateTime EndsAt, bool IsTrial);

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var now = _clock.GetUtcNow().UtcDateTime;
        var until = now.AddDays(opts.LeadDays);

        var activeLinks = _context.ParentStudentLinks.Where(ParentChildAccess.GrantsAccess);
        var candidates = await (
            from a in _context.UserSubscriptionPlans
            where a.EndingReminderSentAtUtc == null && a.ValidToUtc != null && a.ValidToUtc > now && a.ValidToUtc <= until
                && a.Plan.Key != SubscriptionPlan.FreeKey
                && (a.Plan.Audience == SubscriptionPlanAudience.Student || a.Plan.Audience == SubscriptionPlanAudience.Any)
                && !_context.UserSubscriptionPlans.Any(next => next.UserId == a.UserId && next.Id != a.Id && next.ValidFromUtc >= a.ValidToUtc)
            from s in _context.Students
            where s.UserId == a.UserId && !s.IsDeleted
                && activeLinks.Any(l => l.StudentId == s.Id)
            orderby a.ValidToUtc, a.Id
            select new Candidate(a.Id, s.Id, s.UserId, a.ValidToUtc!.Value, a.Plan.TrialDays != null))
            .Take(opts.BatchSize)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return 0;

        var studentIds = candidates.Select(c => c.StudentId).Distinct().ToList();
        var links = await activeLinks.AsNoTracking()
            .Where(l => studentIds.Contains(l.StudentId))
            .Select(l => new { l.StudentId, l.ParentId, ParentUserId = l.Parent.UserId })
            .ToListAsync(ct);
        var parentsByStudent = links.ToLookup(l => l.StudentId);
        // Issue #1092: okul lisansı atamanın bitişinden sonra da sürüyorsa çocuk planını kaybetmez: hatırlatma yok, atama işaretlenir
        // (işaretsiz kalsa her tur partinin başını doldururdu).
        var licenses = await SchoolLicensePlans.ForUsersAsync(_context, candidates.Select(c => c.StudentUserId).ToList(), now, ct);
        var coveredByLicense = candidates
            .Where(c => licenses.TryGetValue(c.StudentUserId, out var l) && (l.Plan.ValidToUtc is null || l.Plan.ValidToUtc >= c.EndsAt))
            .Select(c => c.AssignmentId)
            .ToHashSet();
        var users = await ParentNotificationSupport.LookupAsync(
            _authApi, links.Select(l => l.ParentUserId).Concat(candidates.Select(c => c.StudentUserId)), _logger, ct);

        var processed = 0;
        var notified = 0;
        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
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

                    var stillActive = await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, c.StudentId, ct);
                    var recipients = coveredByLicense.Contains(c.AssignmentId)
                        ? []
                        : parentsByStudent[c.StudentId].Where(p => stillActive.Contains(p.ParentId)).ToList();

                    // Koşullu işaret: eşzamanlı ikinci örnek 0 satır günceller ve event yazmaz.
                    var marked = await _context.UserSubscriptionPlans
                        .Where(a => a.Id == c.AssignmentId && a.EndingReminderSentAtUtc == null)
                        .ExecuteUpdateAsync(u => u.SetProperty(a => a.EndingReminderSentAtUtc, now), ct);
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
                                IsTrial = c.IsTrial,
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
                _logger.LogError(ex, "[ParentPlanEnding] AssignmentId={AssignmentId} işlenemedi; sonraki tura kalır.", c.AssignmentId);
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }

        if (processed > 0)
            _logger.LogInformation("[ParentPlanEnding] {Processed} atama işlendi; {Notified} veli event'i yazıldı.", processed, notified);
        return processed;
    }
}

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Parents;

/// <summary>issue #423: gecikmiş ödev süpürücüsünün ayarları.</summary>
public sealed class ParentHomeworkOverdueSweepOptions
{
    public const string SectionName = "ParentHomeworkOverdueSweep";

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan: 15 dakikada bir.</summary>
    [Required]
    public string Cron { get; set; } = "*/15 * * * *";

    /// <summary>
    /// Süresi bu kadar gün öncesine kadar geçmiş atamalar taranır; job daha uzun süre çalışmazsa daha eski gecikmeler
    /// bildirilmez (bayat bildirim yerine sessiz kayıp bilinçli tercih). Tarama penceresini dar tutar.
    /// </summary>
    [Range(1, 30)]
    public int LookbackDays { get; set; } = 3;

    /// <summary>Tek turda okunacak en fazla (atama, öğrenci) satırı; kalanı sonraki tura kalır (tavana ulaşılırsa uyarı loglanır).</summary>
    [Range(1, MaxBatchSize)]
    public int BatchSize { get; set; } = 200;

    public const int MaxBatchSize = 2_000;
}

/// <summary>issue #423: süresi geçen, tamamlanmamış ödevler için velilere <see cref="ParentHomeworkOverdueEvent"/> yazan Hangfire recurring job.</summary>
public interface IParentHomeworkOverdueSweepJob
{
    /// <summary>
    /// İşlenen (test, öğrenci) çifti sayısını döner. Retry yok: sonraki cron turu zaten yeniden dener (işaretçi
    /// tekilliği sayesinde yinelenen bildirim üretilmez). Filtreler arayüzde.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> SweepAsync(CancellationToken ct = default);
}

/// <summary>
/// issue #423 (epic #407 V5): ödev gecikme bildirimi. Kural veli panelindeki "Gecikmiş" kovasıyla AYNIDIR
/// (<see cref="ParentAssignmentScope.ToBucket"/> + <see cref="AssignmentInstanceWindow"/> + <see cref="AssignmentStudentStatusRules"/>, #367):
/// <list type="bullet">
/// <item>Aday: <c>EndAt</c>'i geçmiş (<see cref="ParentHomeworkOverdueSweepOptions.LookbackDays"/> penceresinde), silinmemiş test, öğrenci için
/// görünür atama (<see cref="WorksheetStudentAccess.AssignmentVisibleTo"/> ile AYNI kural — SQL'de satır içi yazılır; ikisi birlikte
/// değişmeli, kapsam testleri kombinasyonları sürer; okul yalnız doğrulanmış üyelikte sayılır).</item>
/// <item>Test başına TEK sonuç (aynı test birden çok atanmış olabilir): atamalardan biri tamamlandıysa ya da hâlâ yapılabilir bir
/// atama varsa bildirim yok; yalnız tümü Gecikmiş ise bildirilir. Atamadan ÖNCE bitirilmiş test de "yapıldı" sayılır (#367).
/// Süresi dolarak kapanan oturum gecikmiş sayılır ve metin "süresi doldu" olur (<see cref="ParentHomeworkOverdueEvent.InstanceExpired"/>).</item>
/// <item>Yalnız Active bağlantılı veliler; bağlantısı bitiş anından SONRA aktifleşen veli bildirilmez. Active listesi event'leri yazan
/// transaction içinde <c>FOR SHARE</c> ile yeniden okunur ve planla kesiştirilir (koparma yarışı, security M1).</item>
/// <item>(Test, öğrenci) başına en fazla bir kez: <see cref="ParentHomeworkOverdueMarker"/> tekil index'i, event'lerle AYNI transaction'da
/// yazılır (tamamlanmış çift de 0 veliyle işaretlenir → tekrar taranmaz). Eşzamanlı ikinci örnek unique ihlaliyle atlar; hata veren
/// çift loglanır ve diğerlerini engellemez.</item>
/// </list>
/// Aday seçimi Active bağlantısı olan öğrencilerden başlar; velisi olmayan öğrenciler hiç taranmaz.
/// </summary>
public sealed class ParentHomeworkOverdueSweepJob : IParentHomeworkOverdueSweepJob
{
    public const string RecurringJobId = "parent-homework-overdue-sweep";

    private readonly AppDbContext _context;
    private readonly IAuthApiClient? _authApi;
    private readonly IOptionsMonitor<ParentHomeworkOverdueSweepOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ParentHomeworkOverdueSweepJob> _logger;

    public ParentHomeworkOverdueSweepJob(
        AppDbContext context,
        IAuthApiClient? authApi,
        IOptionsMonitor<ParentHomeworkOverdueSweepOptions> options,
        TimeProvider? clock = null,
        ILogger<ParentHomeworkOverdueSweepJob>? logger = null)
    {
        _context = context;
        _authApi = authApi;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ParentHomeworkOverdueSweepJob>.Instance;
    }

    private sealed record Candidate(
        int AssignmentId, int WorksheetId, string WorksheetName, DateTime EndAt,
        int StudentId, int StudentUserId, int? GradeId, int? VerifiedSchoolId);

    private sealed record AssignmentLite(
        int Id, int WorksheetId, int? StudentId, int? GradeId, int? SchoolId, bool IsPlatformWide, DateTime StartAt, DateTime? EndAt);

    private sealed record InstanceLite(int InstanceId, DateTime StartTime, WorksheetInstanceStatus Status, DateTime? EndTime);

    private sealed record Decision(ParentAssignmentBucket Bucket, bool Expired);

    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var now = _clock.GetUtcNow().UtcDateTime;
        var windowStart = now.AddDays(-opts.LookbackDays);

        var rows = await (
            from s in _context.Students
            where _context.ParentStudentLinks.Any(l => l.StudentId == s.Id
                && l.Status == ParentStudentLinkStatus.Active && !l.Parent.IsDeleted)
            from a in _context.WorksheetAssignments
            where a.EndAt != null && a.EndAt < now && a.EndAt >= windowStart
                && !a.Worksheet.IsDeleted
                // WorksheetStudentAccess.AssignmentVisibleTo ile aynı kural (okul yalnız doğrulanmış üyelikte).
                && (a.StudentId == s.Id
                    || (a.StudentId == null && a.GradeId != null && a.GradeId == s.GradeId
                        && (a.IsPlatformWide
                            || (a.SchoolId != null && s.SchoolVerifiedAt != null && a.SchoolId == s.SchoolId))))
            where !_context.ParentHomeworkOverdueMarkers.Any(m => m.WorksheetId == a.WorksheetId && m.StudentId == s.Id)
            orderby a.EndAt, a.Id, s.Id
            select new Candidate(a.Id, a.WorksheetId, a.Worksheet.Name, a.EndAt!.Value,
                s.Id, s.UserId, s.GradeId, s.SchoolVerifiedAt != null ? s.SchoolId : null))
            .Take(opts.BatchSize)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return 0;
        if (rows.Count >= opts.BatchSize)
        {
            _logger.LogWarning(
                "[ParentHomeworkOverdue] Tur başına satır tavanına ulaşıldı ({BatchSize}); kalanı sonraki tura kalır. Tavanı ya da cron'u gözden geçirin.",
                opts.BatchSize);
        }

        var pairs = rows.GroupBy(r => (r.WorksheetId, r.StudentId)).ToList();
        var studentIds = pairs.Select(g => g.Key.StudentId).Distinct().ToList();
        var worksheetIds = pairs.Select(g => g.Key.WorksheetId).Distinct().ToList();
        var dashboardFrom = now.AddDays(-ParentAssignmentScope.WindowDays);

        // Tek sonuç kuralı için bu testlerin TÜM görünür atamaları (veli paneliyle aynı 30 gün penceresi).
        var allAssignments = await _context.WorksheetAssignments.AsNoTracking()
            .Where(a => worksheetIds.Contains(a.WorksheetId) && a.StartAt <= now && (a.EndAt == null || a.EndAt >= dashboardFrom)
                && !a.Worksheet.IsDeleted)
            .Select(a => new AssignmentLite(a.Id, a.WorksheetId, a.StudentId, a.GradeId, a.SchoolId, a.IsPlatformWide, a.StartAt, a.EndAt))
            .ToListAsync(ct);
        var instances = (await _context.TestInstances.AsNoTracking()
                .Where(ti => studentIds.Contains(ti.StudentId) && worksheetIds.Contains(ti.WorksheetId))
                .Select(ti => new { ti.Id, ti.StudentId, ti.WorksheetId, ti.StartTime, ti.Status, ti.EndTime })
                .ToListAsync(ct))
            .ToLookup(i => (i.WorksheetId, i.StudentId), i => new InstanceLite(i.Id, i.StartTime, i.Status, i.EndTime));

        var decisions = new Dictionary<(int WorksheetId, int StudentId), Decision>();
        foreach (var g in pairs)
        {
            var any = g.First();
            var visible = allAssignments
                .Where(a => a.WorksheetId == g.Key.WorksheetId
                    && WorksheetStudentAccess.IsAssignmentVisibleTo(a.StudentId, a.GradeId, a.SchoolId, a.IsPlatformWide,
                        any.StudentId, any.GradeId, any.VerifiedSchoolId))
                .ToList();
            decisions[g.Key] = Decide(visible, instances[g.Key].ToList(), now);
        }

        var links = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => studentIds.Contains(l.StudentId) && l.Status == ParentStudentLinkStatus.Active && !l.Parent.IsDeleted)
            .Select(l => new { l.StudentId, l.ParentId, ParentUserId = l.Parent.UserId, l.ActivatedAt, l.CreatedAt })
            .ToListAsync(ct);
        var parentsByStudent = links.ToLookup(l => l.StudentId);
        var studentUserIds = rows.Select(r => (r.StudentId, r.StudentUserId)).Distinct().ToDictionary(x => x.StudentId, x => x.StudentUserId);

        // Ad/sub çözümü tek batch, fail-soft; hedef sub çözülemezse consumer BadgeService verisinden çözer.
        var users = await ParentNotificationSupport.LookupAsync(
            _authApi, links.Select(l => l.ParentUserId).Concat(studentUserIds.Values), _logger, ct);

        var processed = 0;
        var notified = 0;
        foreach (var g in pairs)
        {
            ct.ThrowIfCancellationRequested();
            var decision = decisions[g.Key];
            if (decision.Bucket == ParentAssignmentBucket.Pending)
                continue; // hâlâ yapılabilir başka bir atama var: işaretleme, sonra yeniden değerlendirilir

            var latest = g.OrderByDescending(r => r.EndAt).First();
            var overdue = decision.Bucket == ParentAssignmentBucket.Overdue;
            var eligible = overdue
                ? parentsByStudent[g.Key.StudentId].Where(p => (p.ActivatedAt ?? p.CreatedAt) < latest.EndAt).ToList()
                : [];
            var student = studentUserIds.TryGetValue(g.Key.StudentId, out var suid)
                ? ParentNotificationSupport.Of(users, suid)
                : NotificationUser.Unknown;

            try
            {
                var written = 0;
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    _context.ChangeTracker.Clear();
                    written = 0;
                    await using var tx = await _context.Database.BeginTransactionAsync(ct);

                    // Security M1: koparma yarışı — plan ile transaction arasında bağlantı koparıldıysa o veliye yazılmaz.
                    var stillActive = eligible.Count == 0
                        ? new HashSet<int>()
                        : await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, g.Key.StudentId, ct);
                    var recipients = eligible.Where(p => stillActive.Contains(p.ParentId)).ToList();

                    _context.ParentHomeworkOverdueMarkers.Add(new ParentHomeworkOverdueMarker
                    {
                        WorksheetId = g.Key.WorksheetId,
                        StudentId = g.Key.StudentId,
                        ProcessedAt = now,
                        NotifiedParentCount = recipients.Count
                    });
                    foreach (var p in recipients)
                    {
                        _context.OutboxMessages.Add(new OutboxMessage
                        {
                            Type = OutboxEventRegistry.NameFor<ParentHomeworkOverdueEvent>(),
                            Content = JsonSerializer.Serialize(new ParentHomeworkOverdueEvent
                            {
                                EventId = Guid.NewGuid(),
                                AssignmentId = latest.AssignmentId,
                                InstanceExpired = decision.Expired,
                                WorksheetId = g.Key.WorksheetId,
                                WorksheetName = latest.WorksheetName,
                                StudentId = g.Key.StudentId,
                                StudentDisplayName = student.DisplayName,
                                ParentId = p.ParentId,
                                ParentUserId = p.ParentUserId,
                                ParentKeycloakId = ParentNotificationSupport.Of(users, p.ParentUserId).KeycloakId,
                                DueAtUtc = DateTime.SpecifyKind(latest.EndAt, DateTimeKind.Utc)
                            }),
                            CreatedAt = now
                        });
                    }
                    await _context.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    written = recipients.Count;
                });
                processed++;
                notified += written;
            }
            catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
            {
                // Başka bir örnek bu çifti işledi — idempotent no-op.
                _logger.LogInformation(
                    "[ParentHomeworkOverdue] WorksheetId={WorksheetId} StudentId={StudentId} zaten işlenmiş; atlanıyor.",
                    g.Key.WorksheetId, g.Key.StudentId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Tek çiftin hatası turun geri kalanını engellemez; işaretçi yazılmadığından sonraki turda yeniden denenir.
                _logger.LogError(ex,
                    "[ParentHomeworkOverdue] WorksheetId={WorksheetId} StudentId={StudentId} işlenemedi; sonraki tura kalır.",
                    g.Key.WorksheetId, g.Key.StudentId);
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }

        if (processed > 0)
        {
            _logger.LogInformation(
                "[ParentHomeworkOverdue] {Processed} (test, öğrenci) çifti işlendi; {Notified} veli event'i yazıldı.",
                processed, notified);
        }

        return processed;
    }

    /// <summary>
    /// Veli paneliyle aynı karar: her görünür atama için ilgili instance + durum → kova; test başına en yüksek kova
    /// (Tamamlandı &gt; Beklemede &gt; Gecikmiş). Görünür liste boşsa (kural değişikliği/silinme) güvenli yön: Completed (bildirme).
    /// </summary>
    private static Decision Decide(IReadOnlyList<AssignmentLite> visible, IReadOnlyList<InstanceLite> instances, DateTime now)
    {
        if (visible.Count == 0)
            return new Decision(ParentAssignmentBucket.Completed, false);

        var classified = ParentAssignmentScope.Classify(
            visible.Select(a => new ParentAssignmentScope.AssignmentRow(a.Id, a.WorksheetId, a.StartAt, a.EndAt, null)).ToList(),
            instances.Select(i => new ParentAssignmentScope.InstanceRow(i.InstanceId, visible[0].WorksheetId, i.StartTime, i.Status, i.EndTime)).ToList(),
            now).Single();

        return new Decision(classified.Bucket, classified.Bucket == ParentAssignmentBucket.Overdue
            && classified.Instance?.Status == WorksheetInstanceStatus.Expired);
    }
}

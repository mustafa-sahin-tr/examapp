using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.TargetGoals;
using ExamApp.Api.Helpers;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.GradePromotion;

/// <summary>Issue #1110: 1 Ağustos sınıf atlamasının ayarları.</summary>
public sealed class StudentGradePromotionOptions
{
    public const string SectionName = "StudentGradePromotion";

    /// <summary>Hangfire recurring job cron'u (UTC). Varsayılan her gece 21:10 UTC = 00:10 İstanbul; iş yalnız atlama haftasında yapılır.</summary>
    [Required]
    public string Cron { get; set; } = "10 21 * * *";

    /// <summary>1 Ağustos dahil bu kadar gün job çalışır: sunucu 1 Ağustos'ta kapalıysa sonraki gecelerde telafi edilir.</summary>
    [Range(1, 31)]
    public int CatchUpDays { get; set; } = 7;

    /// <summary>Tek sorguda okunan en fazla öğrenci; tur bütün öğrencileri bu boyutta partilerle işler.</summary>
    [Range(1, 5_000)]
    public int BatchSize { get; set; } = 500;
}

/// <summary>Issue #1110: her yıl 1 Ağustos'ta (Europe/Istanbul) öğrencileri bir üst sınıfa taşıyan Hangfire job.</summary>
public interface IStudentGradePromotionJob
{
    /// <summary>Bu turda atlatılan öğrenci sayısını döner. Retry yok: sonraki gece zaten yeniden dener.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #1110. Kurallar:
/// <list type="bullet">
/// <item>Yalnız 1 Ağustos'tan itibaren <see cref="StudentGradePromotionOptions.CatchUpDays"/> gün; yıl = 1 Ağustos'un yılı.</item>
/// <item>Aday: silinmemiş, sınıfı 1–11 olan ve 1 Ağustos'tan ÖNCE kaydolmuş öğrenci (sonra kaydolan sınıfını zaten yeni yıla göre
/// seçti). 12. sınıf yerinde kalır (YKS bandı). Sınıf seviyesi kodundan ya da adından (<see cref="TargetGoalService.GradeLevelOf"/>);
/// seviyesi okunamayan ya da üst sınıfı tanımlı olmayan sınıf atlatılmaz.</item>
/// <item>İdempotent: öğrenci + yıl başına tek <see cref="StudentGradePromotion"/> (tekil indeks); satır sınıf değişikliğiyle AYNI
/// transaction'da yazılır. Sınıf arada değiştiyse (koşullu güncelleme 0 satır) öğrenci atlanmaz.</item>
/// <item>Bildirim: atlatılan öğrenciye ve o an Active her velisine birer <see cref="StudentGradePromotedEvent"/>, sınıf değişikliğiyle
/// AYNI transaction'da (veli listesi transaction içinde <c>FOR SHARE</c> ile yeniden okunur, #423). Ad / sub auth-api'den parti
/// başına best-effort çözülür; çözülemezse event boş değerlerle yazılır, consumer yedeğe düşer.</item>
/// <item>Ödenmiş dönem fiyatını korur (atamaya dokunulmaz); yeni bandın fiyatı yenilemede uygulanır, çünkü teklif öğrencinin o
/// anki sınıfından hesaplanır (<c>StudentPlanPricing</c>).</item>
/// </list>
/// </summary>
public sealed class StudentGradePromotionJob : IStudentGradePromotionJob
{
    public const string RecurringJobId = "student-grade-promotion";

    private readonly AppDbContext _context;
    private readonly ILocalDayCalendar _calendar;
    private readonly IOptionsMonitor<StudentGradePromotionOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentGradePromotionJob> _logger;
    private readonly IAuthApiClient? _authApi;

    public StudentGradePromotionJob(
        AppDbContext context,
        ILocalDayCalendar calendar,
        IOptionsMonitor<StudentGradePromotionOptions> options,
        TimeProvider? clock = null,
        ILogger<StudentGradePromotionJob>? logger = null,
        IAuthApiClient? authApi = null)
    {
        _authApi = authApi;
        _context = context;
        _calendar = calendar;
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<StudentGradePromotionJob>.Instance;
    }

    /// <summary>Bugün atlama haftasındaysa yılı, değilse null.</summary>
    public static int? PromotionYearOf(DateOnly today, int catchUpDays)
    {
        var start = new DateOnly(today.Year, 8, 1);
        return today >= start && today < start.AddDays(catchUpDays) ? today.Year : null;
    }

    /// <summary>Bir üst sınıf: id ve seviyesi (bildirim metni).</summary>
    internal sealed record NextGrade(int GradeId, int Level);

    private sealed record ParentRecipient(int ParentId, int ParentUserId);

    private sealed record Recipients(
        int StudentUserId, IReadOnlyList<ParentRecipient> Parents, IReadOnlyDictionary<int, NotificationUser> Users);

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        if (PromotionYearOf(_calendar.Today, opts.CatchUpDays) is not { } year)
            return 0;

        var cutoffUtc = _calendar.StartOfDayUtc(new DateOnly(year, 8, 1));
        var next = await NextGradesAsync(_context, ct);
        if (next.Count == 0)
            return 0;

        var fromGradeIds = next.Keys.ToList();
        var now = _clock.GetUtcNow().UtcDateTime;
        var promoted = 0;
        var lastId = 0;
        while (true)
        {
            var batch = await _context.Students.AsNoTracking()
                .Where(s => s.Id > lastId && s.GradeId != null && fromGradeIds.Contains(s.GradeId.Value) && s.CreateTime < cutoffUtc
                    && !_context.StudentGradePromotions.Any(p => p.StudentId == s.Id && p.Year == year))
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, GradeId = s.GradeId!.Value, s.UserId })
                .Take(opts.BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;

            var studentIds = batch.Select(s => s.Id).ToList();
            var links = await _context.ParentStudentLinks.AsNoTracking()
                .Where(ParentChildAccess.GrantsAccess)
                .Where(l => studentIds.Contains(l.StudentId))
                .Select(l => new { l.StudentId, l.ParentId, ParentUserId = l.Parent.UserId })
                .ToListAsync(ct);
            var parentsByStudent = links.ToLookup(l => l.StudentId, l => new ParentRecipient(l.ParentId, l.ParentUserId));
            var users = await ParentNotificationSupport.LookupAsync(
                _authApi, links.Select(l => l.ParentUserId).Concat(batch.Select(s => s.UserId)), _logger, ct);

            foreach (var s in batch)
            {
                ct.ThrowIfCancellationRequested();
                var recipients = new Recipients(s.UserId, parentsByStudent[s.Id].ToList(), users);
                if (await PromoteAsync(s.Id, s.GradeId, next[s.GradeId], year, now, recipients, ct))
                    promoted++;
            }
            lastId = batch[^1].Id;
        }

        _logger.LogInformation("Sınıf atlaması {Year}: {Count} öğrenci üst sınıfa taşındı.", year, promoted);
        return promoted;
    }

    private async Task<bool> PromoteAsync(
        int studentId, int fromGradeId, NextGrade to, int year, DateTime now, Recipients recipients, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var tx = await _context.Database.BeginTransactionAsync(ct);

                var moved = await _context.Students
                    .Where(s => s.Id == studentId && s.GradeId == fromGradeId)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.GradeId, to.GradeId), ct);
                if (moved == 0)
                {
                    await tx.RollbackAsync(ct);
                    return false;
                }

                _context.StudentGradePromotions.Add(new StudentGradePromotion
                {
                    StudentId = studentId,
                    Year = year,
                    FromGradeId = fromGradeId,
                    ToGradeId = to.GradeId,
                    PromotedAtUtc = now,
                });
                await AddNotificationsAsync(studentId, to.Level, year, now, recipients, ct);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            });
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            // Eşzamanlı ikinci örnek aynı öğrenciyi önce atlattı; bizim sınıf güncellememiz geri alındı.
            _context.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task AddNotificationsAsync(int studentId, int toLevel, int year, DateTime now, Recipients r, CancellationToken ct)
    {
        void Add(string audience, int recipientUserId, string studentName) => _context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<StudentGradePromotedEvent>(),
            Content = JsonSerializer.Serialize(new StudentGradePromotedEvent
            {
                EventId = Guid.NewGuid(),
                Audience = audience,
                RecipientUserId = recipientUserId,
                RecipientKeycloakId = ParentNotificationSupport.Of(r.Users, recipientUserId).KeycloakId,
                StudentId = studentId,
                StudentDisplayName = studentName,
                Year = year,
                ToGradeLevel = toLevel,
                PromotedAtUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc),
            }),
            CreatedAt = now
        });

        Add(StudentGradePromotedEvent.StudentAudience, r.StudentUserId, string.Empty);
        if (r.Parents.Count == 0)
            return;
        // Security M1 (#423): yalnız hem partide hem transaction içi okumada Active olan veliler.
        var stillActive = await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, studentId, ct);
        var studentName = ParentNotificationSupport.Of(r.Users, r.StudentUserId).DisplayName;
        foreach (var p in r.Parents.Where(p => stillActive.Contains(p.ParentId)))
            Add(StudentGradePromotedEvent.ParentAudience, p.ParentUserId, studentName);
    }

    /// <summary>Sınıf id → bir üst sınıf (1–11). Aynı seviyede birden çok sınıf varsa üst sınıf en küçük id'lidir.</summary>
    internal static async Task<Dictionary<int, NextGrade>> NextGradesAsync(AppDbContext context, CancellationToken ct)
    {
        var grades = (await context.Grades.AsNoTracking()
                .Select(g => new { g.Id, g.Code, g.Name })
                .ToListAsync(ct))
            .Select(g => (g.Id, Level: TargetGoalService.GradeLevelOf(g.Code) ?? TargetGoalService.GradeLevelOf(g.Name)))
            .Where(g => g.Level != null)
            .ToList();
        var firstOfLevel = grades
            .GroupBy(g => g.Level!.Value)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Id));

        return grades
            .Where(g => g.Level < 12 && firstOfLevel.ContainsKey(g.Level!.Value + 1))
            .ToDictionary(g => g.Id, g => new NextGrade(firstOfLevel[g.Level!.Value + 1], g.Level!.Value + 1));
    }
}

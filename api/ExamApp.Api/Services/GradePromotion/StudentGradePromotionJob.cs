using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.TargetGoals;
using ExamApp.Api.Helpers;
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

    public StudentGradePromotionJob(
        AppDbContext context,
        ILocalDayCalendar calendar,
        IOptionsMonitor<StudentGradePromotionOptions> options,
        TimeProvider? clock = null,
        ILogger<StudentGradePromotionJob>? logger = null)
    {
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
                .Select(s => new { s.Id, GradeId = s.GradeId!.Value })
                .Take(opts.BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;

            foreach (var s in batch)
            {
                ct.ThrowIfCancellationRequested();
                if (await PromoteAsync(s.Id, s.GradeId, next[s.GradeId], year, now, ct))
                    promoted++;
            }
            lastId = batch[^1].Id;
        }

        _logger.LogInformation("Sınıf atlaması {Year}: {Count} öğrenci üst sınıfa taşındı.", year, promoted);
        return promoted;
    }

    private async Task<bool> PromoteAsync(int studentId, int fromGradeId, int toGradeId, int year, DateTime now, CancellationToken ct)
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
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.GradeId, toGradeId), ct);
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
                    ToGradeId = toGradeId,
                    PromotedAtUtc = now,
                });
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

    /// <summary>Sınıf id → bir üst sınıfın id'si (1–11). Aynı seviyede birden çok sınıf varsa üst sınıf en küçük id'lidir.</summary>
    internal static async Task<Dictionary<int, int>> NextGradesAsync(AppDbContext context, CancellationToken ct)
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
            .ToDictionary(g => g.Id, g => firstOfLevel[g.Level!.Value + 1]);
    }
}

using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Entitlements;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.StudyPlans;

/// <summary>Issue #1113: haftalık çalışma planı job'ının ayarları.</summary>
public sealed class StudyPlanOptions
{
    public const string SectionName = "StudyPlan";

    /// <summary>Hangfire cron'u (UTC). Varsayılan Pazartesi 03:00 UTC (06:00 İstanbul).</summary>
    [Required]
    public string Cron { get; set; } = "0 3 * * 1";

    [Range(1, 2_000)]
    public int BatchSize { get; set; } = 200;
}

public interface IStudyPlanJob
{
    /// <summary>Kurulan plan sayısı. Planı olan atlanır; öğrencinin ilk açılışı da kurar, retry gerekmez.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #1113: Pazartesi sabahı planında <see cref="EntitlementFeatures.WeeklyPlan"/> olan her öğrenciye bu haftanın planını kurar
/// (bildirimde ve veli panelinde hafta başından görünsün). Öğrenciler sayfa sayfa gezilir; plan sonradan alınırsa ilk açılış kurar.
/// </summary>
public sealed class StudyPlanJob(
    AppDbContext db,
    IStudyPlanService plans,
    IEntitlementService entitlements,
    Microsoft.Extensions.Options.IOptions<StudyPlanOptions> options,
    ILogger<StudyPlanJob> logger) : IStudyPlanJob
{
    public const string RecurringJobId = "weekly-study-plan";

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var created = 0;
        var lastId = 0;
        var batchSize = options.Value.BatchSize;
        while (true)
        {
            var students = await db.Students.AsNoTracking()
                .Where(s => s.Id > lastId && s.UserId > 0)
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.UserId })
                .Take(batchSize)
                .ToListAsync(ct);
            if (students.Count == 0)
                break;
            lastId = students[^1].Id;

            foreach (var student in students)
            {
                var check = await entitlements.CheckAsync(EntitlementSubject.Create(student.UserId), EntitlementFeatures.WeeklyPlan, ct);
                if (check.Limit != 0 && await plans.EnsureAsync(student.Id, ct))
                    created++;
            }
            if (students.Count < batchSize)
                break;
        }

        if (created > 0)
            logger.LogInformation("[StudyPlan] {Week} haftası için {Count} çalışma planı kuruldu.", plans.CurrentWeek, created);
        return created;
    }
}

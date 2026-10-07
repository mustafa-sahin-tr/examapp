using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// <inheritdoc cref="IParentDashboardService"/>
/// <para>
/// Veri kaynakları öğrencinin kendi ekranlarıyla aynı tanımlardır, ayrı bir "veli" kuralı yoktur:
/// <list type="bullet">
/// <item>Çözülen soru: worksheet cevabı (<c>SelectedAnswerId/AnswerPayload</c> dolu, an = <c>UpdateTime</c>; DashboardService /
/// DailyQuestionSetService ile aynı) + pratik cevabı (<c>AnsweredAt</c>, pas hariç — DailyQuestionSetService ile aynı).</item>
/// <item>Hafta: yerel takvim (<see cref="ILocalDayCalendar"/>, Dashboard:TimeZone = Europe/Istanbul), Pazartesi 00:00'dan.</item>
/// <item>Atama görünürlüğü: <see cref="WorksheetStudentAccess.AssignmentVisibleTo"/> (doğrulanmış okul, #361); ilgili
/// instance <see cref="AssignmentInstanceWindow"/>; durum <see cref="AssignmentStudentStatusRules"/> (öğretmen ilerleme
/// ekranıyla aynı).</item>
/// <item>Puan: <c>StudentPoints.XP</c> toplamı (öğrenci profili ile aynı).</item>
/// </list>
/// </para>
/// </summary>
public sealed class ParentDashboardService : IParentDashboardService
{
    /// <summary>Teslim tarihi geçmiş atamalar bu kadar gün geriye bakılarak sayılır (eski gecikmeler sonsuza kadar birikmesin).</summary>
    internal const int AssignmentWindowDays = 30;

    /// <summary>Sayım için okunan en fazla atama satırı (güvenlik tavanı; gerçekçi bir öğrencide ulaşılmaz).</summary>
    internal const int MaxAssignmentRows = 500;

    private readonly AppDbContext _context;
    private readonly IParentChildAccess _access;
    private readonly IParentAccessAuditLog _audit;
    private readonly ILocalDayCalendar _calendar;
    private readonly TimeProvider _time;
    private readonly ILogger<ParentDashboardService>? _logger;

    public ParentDashboardService(
        AppDbContext context,
        IParentChildAccess access,
        IParentAccessAuditLog audit,
        ILocalDayCalendar? calendar = null,
        TimeProvider? time = null,
        ILogger<ParentDashboardService>? logger = null)
    {
        _context = context;
        _access = access;
        _audit = audit;
        _calendar = calendar ?? LocalDayCalendar.Default;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<ParentChildSummaryDto?> GetChildSummaryAsync(int parentUserId, int studentId, CancellationToken ct = default)
    {
        var grant = await _access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant == null)
            return null;

        await _audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildSummary, ct);

        var now = _time.GetUtcNow().UtcDateTime;
        var weekStart = StartOfWeek(_calendar.Today);
        var weekStartUtc = _calendar.StartOfDayUtc(weekStart);
        var weekEndUtc = _calendar.StartOfDayUtc(weekStart.AddDays(7));

        // AppDbContext thread-safe değil: sorgular art arda (her biri tek COUNT/SUM/MAX; N+1 yok).
        var solved = await CountSolvedAsync(grant.StudentId, weekStartUtc, weekEndUtc, ct);
        var assignments = await CountAssignmentsAsync(grant, now, ct);
        var points = await _context.StudentPoints.AsNoTracking()
            .Where(sp => sp.StudentId == grant.StudentId)
            .SumAsync(sp => (int?)sp.XP, ct) ?? 0;
        var lastActivity = await LastActivityAsync(grant.StudentId, ct);

        return new ParentChildSummaryDto
        {
            StudentId = grant.StudentId,
            WeekStart = weekStart,
            QuestionsSolvedThisWeek = solved,
            Assignments = assignments,
            TotalPoints = points,
            LastActivityAt = lastActivity
        };
    }

    /// <summary>Haftanın Pazartesi'si (ISO haftası; Pazar bir önceki Pazartesi'ye bağlanır).</summary>
    internal static DateOnly StartOfWeek(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private async Task<int> CountSolvedAsync(int studentId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // IX_TestInstanceQuestions_UpdateTime_Answered yüklemiyle aynı şekil (cevaplı + UpdateTime aralığı).
        var worksheetAnswers = await _context.TestInstanceQuestions.AsNoTracking()
            .Where(q => q.WorksheetInstance.StudentId == studentId
                && (q.SelectedAnswerId != null || q.AnswerPayload != null)
                && q.UpdateTime != null && q.UpdateTime >= fromUtc && q.UpdateTime < toUtc)
            .CountAsync(ct);

        var practiceAnswers = await _context.PracticeSessionQuestions.AsNoTracking()
            .Where(p => p.PracticeSession.StudentId == studentId
                && p.AnsweredAt != null && p.AnsweredAt >= fromUtc && p.AnsweredAt < toUtc && !p.IsSkipped)
            .CountAsync(ct);

        return worksheetAnswers + practiceAnswers;
    }

    private async Task<ParentChildAssignmentCountsDto> CountAssignmentsAsync(ParentChildAccessGrant grant, DateTime now, CancellationToken ct)
    {
        var lookbackStart = now.AddDays(-AssignmentWindowDays);

        // Başlamış atamalar: açık olanlar + teslim tarihi pencere içinde geçmiş olanlar. Geri çekilen (soft-delete) atama ve
        // emekliye ayrılan (soft-delete) worksheet global filtreyle düşer — öğrencinin kendi listesinde de görünmezler.
        var assignments = await _context.WorksheetAssignments.AsNoTracking()
            .Where(WorksheetStudentAccess.AssignmentVisibleTo(grant.StudentId, grant.GradeId, grant.VerifiedSchoolId))
            .Where(a => a.StartAt <= now && (a.EndAt == null || a.EndAt >= lookbackStart))
            .Where(a => !a.Worksheet.IsDeleted)
            .OrderByDescending(a => a.StartAt).ThenByDescending(a => a.Id)
            .Take(MaxAssignmentRows)
            .Select(a => new AssignmentRow(a.WorksheetId, a.StartAt, a.EndAt))
            .ToListAsync(ct);

        if (assignments.Count >= MaxAssignmentRows)
        {
            // Sayımlar en yeni MaxAssignmentRows atamayla sınırlı — gerçekçi bir öğrencide beklenmez; görünür olsun.
            _logger?.LogWarning(
                "[ParentDashboard] Atama satırı tavanına ulaşıldı ({Cap}); özet sayıları kesilmiş olabilir: studentId={StudentId}",
                MaxAssignmentRows, grant.StudentId);
        }

        var counts = new ParentChildAssignmentCountsDto { WindowDays = AssignmentWindowDays };
        if (assignments.Count == 0)
            return counts;

        var worksheetIds = assignments.Select(a => a.WorksheetId).Distinct().ToList();
        var instances = await _context.TestInstances.AsNoTracking()
            .Where(ti => ti.StudentId == grant.StudentId && worksheetIds.Contains(ti.WorksheetId))
            .Select(ti => new InstanceRow(ti.WorksheetId, ti.StartTime, ti.Status, ti.EndTime))
            .ToListAsync(ct);

        foreach (var bucket in Classify(assignments, instances, now))
        {
            switch (bucket)
            {
                case ParentAssignmentBucket.Completed: counts.Completed++; break;
                case ParentAssignmentBucket.Overdue: counts.Overdue++; break;
                default: counts.Pending++; break;
            }
        }

        return counts;
    }

    /// <summary>
    /// Worksheet başına TEK sonuç (aynı worksheet hem doğrudan hem sınıfa atanmış olabilir — takvimdeki tekilleştirme gibi):
    /// atamalardan biri tamamlandıysa Completed; değilse hâlâ yapılabilir bir atama varsa Pending; aksi halde Overdue.
    /// </summary>
    internal static IEnumerable<ParentAssignmentBucket> Classify(
        IReadOnlyList<AssignmentRow> assignments, IReadOnlyList<InstanceRow> instances, DateTime now)
    {
        var instancesByWorksheet = instances.ToLookup(i => i.WorksheetId);
        return assignments
            .GroupBy(a => a.WorksheetId)
            .Select(g => g
                .Select(a =>
                {
                    var relevant = AssignmentInstanceWindow.SelectRelevant(
                        instancesByWorksheet[a.WorksheetId], a.StartAt, a.EndAt, i => i.StartTime, i => i.Status);
                    var status = AssignmentStudentStatusRules.Resolve(a.StartAt, a.EndAt, relevant?.Status, relevant?.EndTime, now);
                    return ToBucket(status, a.EndAt, now);
                })
                .Max());
    }

    /// <summary>
    /// Öğretmen durumundan veli kovasına: Completed → Completed; Expired (teslim tarihi geçti ya da oturumun süresi doldu) →
    /// Overdue; başlanmış ama teslim tarihi geçmiş (InProgress) → Overdue; diğerleri (NotStarted / InProgress, süre devam
    /// ediyor) → Pending.
    /// </summary>
    internal static ParentAssignmentBucket ToBucket(string status, DateTime? endAt, DateTime now) => status switch
    {
        AssignmentStudentStatuses.Completed => ParentAssignmentBucket.Completed,
        AssignmentStudentStatuses.Expired => ParentAssignmentBucket.Overdue,
        _ when endAt.HasValue && endAt.Value < now => ParentAssignmentBucket.Overdue,
        _ => ParentAssignmentBucket.Pending
    };

    private async Task<DateTime?> LastActivityAsync(int studentId, CancellationToken ct)
    {
        var lastAnswer = await _context.TestInstanceQuestions.AsNoTracking()
            .Where(q => q.WorksheetInstance.StudentId == studentId
                && (q.SelectedAnswerId != null || q.AnswerPayload != null) && q.UpdateTime != null)
            .MaxAsync(q => q.UpdateTime, ct);
        // Son başlatma + son bitirme tek sorguda (sabit anahtarla gruplanmış iki MAX).
        var instanceTimes = await _context.TestInstances.AsNoTracking()
            .Where(ti => ti.StudentId == studentId)
            .GroupBy(_ => 1)
            .Select(g => new { LastStart = g.Max(ti => (DateTime?)ti.StartTime), LastEnd = g.Max(ti => ti.EndTime) })
            .FirstOrDefaultAsync(ct);
        var lastPractice = await _context.PracticeSessionQuestions.AsNoTracking()
            .Where(p => p.PracticeSession.StudentId == studentId && p.AnsweredAt != null)
            .MaxAsync(p => p.AnsweredAt, ct);

        DateTime? latest = null;
        foreach (var candidate in new[] { lastAnswer, instanceTimes?.LastStart, instanceTimes?.LastEnd, lastPractice })
        {
            if (candidate.HasValue && (latest == null || candidate.Value > latest.Value))
                latest = candidate.Value;
        }

        return latest.HasValue ? TruncateToHour(latest.Value) : null;
    }

    /// <summary>
    /// Review (#420): son aktivite saate KESİLEREK döner (UTC) — veli "bugün / dün + yaklaşık saat" görür, dakika
    /// hassasiyetinde bir hareket izi (ör. gece 02:13'te çevrimiçi) açılmaz.
    /// </summary>
    internal static DateTime TruncateToHour(DateTime value)
        => new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);

    internal sealed record AssignmentRow(int WorksheetId, DateTime StartAt, DateTime? EndAt);

    internal sealed record InstanceRow(int WorksheetId, DateTime StartTime, WorksheetInstanceStatus Status, DateTime? EndTime);
}

/// <summary>Veli özetindeki atama kovası; sıra öncelik verir (worksheet başına en büyüğü sayılır).</summary>
public enum ParentAssignmentBucket
{
    Overdue = 0,
    Pending = 1,
    Completed = 2
}

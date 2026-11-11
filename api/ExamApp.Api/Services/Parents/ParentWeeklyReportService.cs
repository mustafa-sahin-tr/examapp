using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.MockExams;
using ExamApp.Api.Services.TopicPerformance;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Parents;

/// <summary>Issue #1112: velinin haftalık raporu. Plan kapısı (<c>weekly-report</c>) çağırandadır.</summary>
public interface IParentWeeklyReportService
{
    /// <summary>Son biten haftanın Pazartesi'si (yerel).</summary>
    DateOnly LastCompletedWeek { get; }

    /// <summary>Veli ucu: Active bağlantı kapısı + erişim kaydı; bağlı olmayan çocukta null.</summary>
    Task<ParentWeeklyReportDto?> GetForParentAsync(int parentUserId, int studentId, DateOnly weekStart, CancellationToken ct = default);

    /// <summary>Kapıdan geçmiş (ya da job'ın kurduğu) <paramref name="grant"/> için raporu hesaplar.</summary>
    Task<ParentWeeklyReportDto> BuildAsync(ParentChildAccessGrant grant, DateOnly weekStart, CancellationToken ct = default);
}

/// <summary>
/// Issue #1112: haftalık rapor panelin kaynaklarından hesaplanır, saklanmaz (her okuma aynı sayıyı verir; sonradan gelen cevap da
/// girer). Cevaplar <see cref="ParentDashboardService.WeekAnswersAsync"/>, süre <see cref="StudentAnswerReader"/>, ders ve düşen
/// konular <see cref="ITopicPerformanceService"/> (önceki pencere = önceki hafta), ödevler <see cref="ParentAssignmentScope"/>
/// (hafta sonu anına göre), denemeler <see cref="IMockExamService.GetResultsFinishedBetweenAsync"/>, rozet ve puan projeksiyonlardan.
/// </summary>
public sealed class ParentWeeklyReportService(
    AppDbContext context,
    IParentChildAccess access,
    IParentAccessAuditLog audit,
    ITopicPerformanceService topicPerformance,
    IMockExamService mockExams,
    ILocalDayCalendar? calendar = null) : IParentWeeklyReportService
{
    /// <summary>Bir cevabın süreye en çok katkısı (saniye): açık bırakılan sekme haftayı şişirmesin.</summary>
    internal const int MaxSecondsPerAnswer = 600;

    internal const int DropMinAnswers = 3;
    internal const decimal DropMinPoints = 15m;
    internal const int MaxDroppedTopics = 3;

    /// <summary>Öneri: gecikmiş ödev / zayıf konu yoksa haftada bu kadar günden az çalışana "daha çok gün".</summary>
    internal const int TargetActiveDays = 3;

    private readonly ILocalDayCalendar _calendar = calendar ?? LocalDayCalendar.Default;

    public DateOnly LastCompletedWeek => ParentDashboardService.StartOfWeek(_calendar.Today).AddDays(-7);

    public async Task<ParentWeeklyReportDto?> GetForParentAsync(int parentUserId, int studentId, DateOnly weekStart, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant == null)
            return null;

        await audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildWeeklyReport, ct: ct);
        return await BuildAsync(grant, weekStart, ct);
    }

    public async Task<ParentWeeklyReportDto> BuildAsync(ParentChildAccessGrant grant, DateOnly weekStart, CancellationToken ct = default)
    {
        var fromUtc = _calendar.StartOfDayUtc(weekStart);
        var toUtc = _calendar.StartOfDayUtc(weekStart.AddDays(7));

        // AppDbContext thread-safe değil: sorgular art arda.
        var week = await ParentDashboardService.WeekAnswersAsync(context, _calendar.TimeZone, grant.StudentId, fromUtc, toUtc, ct);
        var seconds = (await new StudentAnswerReader(context).ReadAsync(grant.StudentId, fromUtc, toUtc, ct))
            .Sum(a => (long)Math.Clamp(a.TimeTaken, 0, MaxSecondsPerAnswer));
        var points = await context.StudentDailyXps.AsNoTracking()
            .Where(x => x.StudentId == grant.StudentId && x.Day >= weekStart && x.Day < weekStart.AddDays(7))
            .SumAsync(x => (int?)x.Xp, ct) ?? 0;

        var topics = await topicPerformance.GetAsync(grant.StudentId, fromUtc, toUtc, null, TopicPerformanceGroupBy.Topic, 1, ct);
        var subjects = topics.Items
            .Where(i => i.SubjectId != null)
            .GroupBy(i => (Id: i.SubjectId!.Value, Name: i.SubjectName ?? string.Empty))
            .Select(g => new ParentWeeklySubjectDto
            {
                SubjectId = g.Key.Id,
                SubjectName = g.Key.Name,
                Answered = g.Sum(i => i.Correct + i.Wrong),
                Correct = g.Sum(i => i.Correct),
            })
            .Where(s => s.Answered > 0)
            .OrderByDescending(s => s.Answered).ThenBy(s => s.SubjectName)
            .ToList();
        foreach (var s in subjects)
            s.AccuracyPercent = Percent(s.Correct * 100m / s.Answered);
        var dropped = topics.Items
            .Where(i => i.TopicId != null && i.Trend is { } trend && trend <= -DropMinPoints
                && i.PrevAnswered >= DropMinAnswers && i.Correct + i.Wrong >= DropMinAnswers)
            .OrderBy(i => i.Trend).ThenBy(i => i.TopicName)
            .Take(MaxDroppedTopics)
            .Select(i => new ParentWeeklyTopicDropDto
            {
                TopicId = i.TopicId!.Value,
                TopicName = i.TopicName ?? string.Empty,
                SubjectName = i.SubjectName,
                AccuracyPercent = Percent(i.AccuracyPct),
                PreviousAccuracyPercent = Percent(i.PrevAccuracyPct ?? 0),
            })
            .ToList();

        // Panel kovaları hafta sonu anına göre: tamamlananlardan bu hafta biteni, gecikmişlerin hepsi.
        var assignments = (await ParentAssignmentScope.LoadAsync(context, grant, toUtc, ct)).Items;
        var completed = assignments.Count(a => a.Bucket == ParentAssignmentBucket.Completed
            && a.Instance?.EndTime is { } end && end >= fromUtc && end < toUtc);
        var overdue = assignments.Count(a => a.Bucket == ParentAssignmentBucket.Overdue);

        var exams = (await mockExams.GetResultsFinishedBetweenAsync(grant.StudentId, fromUtc, toUtc, ct))
            .Select(r => new ParentWeeklyMockExamDto
            {
                Name = r.Name,
                Net = r.Net,
                Rank = r.Ranking?.Rank,
                Takers = r.Ranking?.Rank is null ? null : r.Ranking.Takers,
                FinishedAtUtc = DateTime.SpecifyKind(r.FinishedAt ?? toUtc, DateTimeKind.Utc),
            })
            .ToList();
        var badges = await context.StudentBadgeProjections.AsNoTracking()
            .Where(b => b.StudentId == grant.StudentId && b.EarnedAtUtc >= fromUtc && b.EarnedAtUtc < toUtc)
            .OrderBy(b => b.EarnedAtUtc).ThenBy(b => b.Id)
            .Select(b => new ParentWeeklyBadgeDto { Name = b.Name, Icon = b.Icon })
            .ToListAsync(ct);

        var report = new ParentWeeklyReportDto
        {
            StudentId = grant.StudentId,
            WeekStart = weekStart,
            QuestionsSolved = week.Solved,
            Correct = week.Correct,
            ActiveDays = week.ActiveDays,
            StudyMinutes = (int)(seconds / 60),
            Points = Math.Max(0, points),
            Subjects = subjects,
            DroppedTopics = dropped,
            AssignmentsCompleted = completed,
            AssignmentsOverdue = overdue,
            MockExams = exams,
            Badges = badges,
        };
        (report.Suggestion, report.SuggestionTopic) = Suggest(report, topics);
        return report;
    }

    /// <summary>
    /// Tek satır, öncelik sırasıyla: hiç cevap yok → <c>noActivity</c>; gecikmiş ödev → <c>overdue</c>; düşen ya da %70'in altında
    /// (en az 5 cevap) konu → <c>reviewTopic</c>; <see cref="TargetActiveDays"/>'ten az gün → <c>moreDays</c>; yoksa <c>keepGoing</c>.
    /// </summary>
    internal static (string Key, string? Topic) Suggest(ParentWeeklyReportDto report, TopicPerformanceDto topics)
    {
        if (report.QuestionsSolved == 0 && report.MockExams.Count == 0)
            return ("noActivity", null);
        if (report.AssignmentsOverdue > 0)
            return ("overdue", null);
        var weak = report.DroppedTopics.FirstOrDefault()?.TopicName
            ?? topics.Items
                .Where(i => i.TopicId != null && i.Correct + i.Wrong >= TopicPerformanceQuery.ParentMinQuestions
                    && i.AccuracyPct < ParentDashboardService.WeakTopicMaxAccuracy)
                .OrderBy(i => i.AccuracyPct).ThenByDescending(i => i.Correct + i.Wrong)
                .Select(i => i.TopicName)
                .FirstOrDefault();
        if (!string.IsNullOrEmpty(weak))
            return ("reviewTopic", weak);
        return report.ActiveDays < TargetActiveDays ? ("moreDays", null) : ("keepGoing", null);
    }

    private static int Percent(decimal value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

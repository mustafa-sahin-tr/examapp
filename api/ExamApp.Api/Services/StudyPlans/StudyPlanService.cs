using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Flashcards;
using ExamApp.Api.Services.MockExams;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Services.TopicPerformance;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.StudyPlans;

public sealed class StudyPlanTaskDto
{
    public int Id { get; set; }
    /// <summary><c>assignment</c>, <c>mockExam</c>, <c>practiceTopic</c>, <c>mistakes</c>, <c>flashcards</c>, <c>practice</c>.</summary>
    public string Kind { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? SubjectName { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? WorksheetId { get; set; }
    public int? MockExamId { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public bool Done { get; set; }
}

public sealed class StudyPlanDto
{
    public DateOnly WeekStart { get; set; }
    public int DoneCount { get; set; }
    public int TotalCount { get; set; }
    public List<StudyPlanTaskDto> Tasks { get; set; } = new();
}

public interface IStudyPlanService
{
    /// <summary>Bu haftanın planı; yoksa kurar. Öğrenci kaydı yoksa null.</summary>
    Task<StudyPlanDto?> GetCurrentForStudentAsync(int userId, CancellationToken ct = default);

    /// <summary>Öğrencinin bu haftaki görevini işaretler; başka öğrencinin ya da geçmiş haftanın görevi null.</summary>
    Task<StudyPlanDto?> SetTaskDoneAsync(int userId, int taskId, bool done, CancellationToken ct = default);

    /// <summary>Veli: çocuğun bu haftaki planı (yoksa kurar). Bağlantı yoksa null.</summary>
    Task<StudyPlanDto?> GetCurrentForParentAsync(int parentUserId, int studentId, CancellationToken ct = default);

    /// <summary>Pazartesi işi: planı yoksa kurar. Kurulduysa true.</summary>
    Task<bool> EnsureAsync(int studentId, CancellationToken ct = default);

    DateOnly CurrentWeek { get; }
}

/// <summary>
/// Issue #1113 (epic #1118): haftalık çalışma planı. Görevler kurallarla seçilir (AI yok), öncelik sırasıyla: süren ödevler (en çok 3,
/// bitişi yakın önce), çözülmemiş bir deneme, zayıf konularda pratik (son 30 gün, en az 5 soru, %70 altı; en çok 3), Yanlışlarım (aktif
/// yanlış varsa), bilgi kartları (destesi varsa); 5'ten azsa en düşük başarılı diğer konular, hâlâ boşsa konu seçmeden pratik. En çok 10.
/// Görev kendiliğinden kapanmaz: öğrenci işaretler.
/// </summary>
public sealed class StudyPlanService(
    AppDbContext db,
    ITopicPerformanceService topicPerformance,
    IMockExamService mockExams,
    IFlashcardStudyService flashcards,
    IParentChildAccess access,
    IParentAccessAuditLog audit,
    TimeProvider clock,
    ILocalDayCalendar? calendar = null) : IStudyPlanService
{
    internal const int MinTasks = 5;
    internal const int MaxTasks = 10;
    internal const int MaxAssignments = 3;
    internal const int MaxWeakTopics = 3;
    internal const int WeakTopicWindowDays = 30;
    internal const int WeakTopicMinQuestions = 5;
    internal const decimal WeakTopicAccuracyPct = 70m;

    private readonly ILocalDayCalendar _calendar = calendar ?? LocalDayCalendar.Default;

    private sealed record StudentRow(int Id, int UserId, int? GradeId, int? VerifiedSchoolId);

    public DateOnly CurrentWeek => ParentDashboardService.StartOfWeek(_calendar.Today);

    public async Task<StudyPlanDto?> GetCurrentForStudentAsync(int userId, CancellationToken ct = default)
    {
        var student = await StudentAsync(s => s.UserId == userId, ct);
        return student is null ? null : await GetOrCreateAsync(student, ct);
    }

    public async Task<StudyPlanDto?> SetTaskDoneAsync(int userId, int taskId, bool done, CancellationToken ct = default)
    {
        var week = CurrentWeek;
        var task = await db.WeeklyStudyPlanTasks
            .Where(t => t.Id == taskId && t.Plan.Student.UserId == userId && t.Plan.WeekStart == week)
            .FirstOrDefaultAsync(ct);
        if (task is null)
            return null;
        if (done != (task.CompletedAtUtc is not null))
        {
            task.CompletedAtUtc = done ? clock.GetUtcNow().UtcDateTime : null;
            await db.SaveChangesAsync(ct);
        }
        return await ReadAsync(task.PlanId, ct);
    }

    public async Task<StudyPlanDto?> GetCurrentForParentAsync(int parentUserId, int studentId, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant is null)
            return null;
        await audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildStudyPlan, ct: ct);
        return await GetOrCreateAsync(new StudentRow(grant.StudentId, grant.StudentUserId, grant.GradeId, grant.VerifiedSchoolId), ct);
    }

    public async Task<bool> EnsureAsync(int studentId, CancellationToken ct = default)
    {
        var week = CurrentWeek;
        if (await db.WeeklyStudyPlans.AnyAsync(p => p.StudentId == studentId && p.WeekStart == week, ct))
            return false;
        var student = await StudentAsync(s => s.Id == studentId, ct);
        if (student is null)
            return false;
        return await CreateAsync(student, week, ct) is not null;
    }

    private Task<StudentRow?> StudentAsync(System.Linq.Expressions.Expression<Func<Student, bool>> filter, CancellationToken ct) =>
        db.Students.AsNoTracking().Where(filter)
            .Select(s => new StudentRow(s.Id, s.UserId, s.GradeId, s.SchoolVerifiedAt != null ? s.SchoolId : null))
            .FirstOrDefaultAsync(ct);

    private async Task<StudyPlanDto> GetOrCreateAsync(StudentRow student, CancellationToken ct)
    {
        var week = CurrentWeek;
        var planId = await db.WeeklyStudyPlans.AsNoTracking()
            .Where(p => p.StudentId == student.Id && p.WeekStart == week).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
        planId ??= await CreateAsync(student, week, ct)
            // Eşzamanlı ikinci istek önce yazdı.
            ?? await db.WeeklyStudyPlans.AsNoTracking().Where(p => p.StudentId == student.Id && p.WeekStart == week).Select(p => p.Id).FirstAsync(ct);
        return (await ReadAsync(planId.Value, ct))!;
    }

    /// <returns>Yeni planın id'si; aynı hafta için başka istek önce yazdıysa null.</returns>
    private async Task<int?> CreateAsync(StudentRow student, DateOnly week, CancellationToken ct)
    {
        var plan = new WeeklyStudyPlan
        {
            StudentId = student.Id,
            WeekStart = week,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            Tasks = (await BuildTasksAsync(student, ct)).Select((t, i) => { t.Order = i + 1; return t; }).ToList(),
        };
        db.WeeklyStudyPlans.Add(plan);
        try
        {
            await db.SaveChangesAsync(ct);
            return plan.Id;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.WeeklyStudyPlans.AsNoTracking().AnyAsync(p => p.StudentId == student.Id && p.WeekStart == week, ct))
                return null;
            throw;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<List<WeeklyStudyPlanTask>> BuildTasksAsync(StudentRow student, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var tasks = new List<WeeklyStudyPlanTask>();

        // AppDbContext thread-safe değil: sorgular art arda.
        var grant = new ParentChildAccessGrant(0, 0, student.Id, student.UserId, student.GradeId, student.VerifiedSchoolId);
        var open = (await ParentAssignmentScope.LoadAsync(db, grant, now, ct)).Items
            .Where(a => a.Bucket is ParentAssignmentBucket.Pending or ParentAssignmentBucket.Overdue)
            .OrderBy(a => a.Assignment.EndAt ?? DateTime.MaxValue)
            .Take(MaxAssignments)
            .ToList();
        var worksheetIds = open.Select(a => a.Assignment.WorksheetId).ToList();
        var names = await db.Worksheets.AsNoTracking().Where(w => worksheetIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Name, SubjectName = w.Subject != null ? w.Subject.Name : null })
            .ToDictionaryAsync(w => w.Id, ct);
        foreach (var a in open)
        {
            names.TryGetValue(a.Assignment.WorksheetId, out var ws);
            tasks.Add(new WeeklyStudyPlanTask
            {
                Kind = StudyPlanTaskKind.Assignment,
                WorksheetId = a.Assignment.WorksheetId,
                Title = Cut(ws?.Name),
                SubjectName = Cut(ws?.SubjectName),
                DueAtUtc = a.Assignment.EndAt,
            });
        }

        var exams = await mockExams.ListForStudentAsync(
            new StudentProfileDto { Id = student.Id, GradeId = student.GradeId, SchoolId = student.VerifiedSchoolId }, ct);
        if (exams.FirstOrDefault(e => e.Sessions.Count == 0) is { } exam)
            tasks.Add(new WeeklyStudyPlanTask { Kind = StudyPlanTaskKind.MockExam, MockExamId = exam.Id, Title = Cut(exam.Name) });

        var topics = (await topicPerformance.GetAsync(student.Id, now.AddDays(-WeakTopicWindowDays), now, null,
                TopicPerformanceGroupBy.Topic, WeakTopicMinQuestions, ct)).Items
            .Where(i => i.TopicId is not null)
            .OrderBy(i => i.AccuracyPct)
            .ToList();
        foreach (var topic in topics.Where(i => i.AccuracyPct < WeakTopicAccuracyPct).Take(MaxWeakTopics))
            tasks.Add(TopicTask(topic));

        var attempts = await DueReviewQueries.LoadAttemptsAsync(db, student.Id, ct);
        if (attempts.Values.Any(a => MistakeNotebookQueries.Evaluate(a) is not null))
            tasks.Add(new WeeklyStudyPlanTask { Kind = StudyPlanTaskKind.Mistakes });

        var learner = new FlashcardLearner(student.Id, student.UserId, student.GradeId, student.VerifiedSchoolId);
        if ((await flashcards.ListDecksAsync(learner, ct)).Count > 0)
            tasks.Add(new WeeklyStudyPlanTask { Kind = StudyPlanTaskKind.Flashcards });

        // Az görev: en düşük başarılı diğer konular, yine azsa konu seçmeden pratik.
        foreach (var topic in topics.Where(i => i.AccuracyPct >= WeakTopicAccuracyPct))
        {
            if (tasks.Count >= MinTasks)
                break;
            tasks.Add(TopicTask(topic));
        }
        if (tasks.Count < MinTasks)
            tasks.Add(new WeeklyStudyPlanTask { Kind = StudyPlanTaskKind.Practice });

        return tasks.Take(MaxTasks).ToList();
    }

    private static WeeklyStudyPlanTask TopicTask(TopicPerformanceItemDto topic) => new()
    {
        Kind = StudyPlanTaskKind.PracticeTopic,
        TopicId = topic.TopicId,
        SubjectId = topic.SubjectId,
        Title = Cut(topic.TopicName),
        SubjectName = Cut(topic.SubjectName),
    };

    private async Task<StudyPlanDto?> ReadAsync(int planId, CancellationToken ct)
    {
        var plan = await db.WeeklyStudyPlans.AsNoTracking().Include(p => p.Tasks).FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null)
            return null;
        var tasks = plan.Tasks.OrderBy(t => t.Order).Select(t => new StudyPlanTaskDto
        {
            Id = t.Id,
            Kind = char.ToLowerInvariant(t.Kind.ToString()[0]) + t.Kind.ToString()[1..],
            Title = t.Title,
            SubjectName = t.SubjectName,
            SubjectId = t.SubjectId,
            TopicId = t.TopicId,
            WorksheetId = t.WorksheetId,
            MockExamId = t.MockExamId,
            DueAtUtc = t.DueAtUtc,
            Done = t.CompletedAtUtc is not null,
        }).ToList();
        return new StudyPlanDto { WeekStart = plan.WeekStart, Tasks = tasks, TotalCount = tasks.Count, DoneCount = tasks.Count(t => t.Done) };
    }

    private static string? Cut(string? value) =>
        value is { Length: > WeeklyStudyPlanTask.MaxTitleLength } ? value[..WeeklyStudyPlanTask.MaxTitleLength] : value;
}

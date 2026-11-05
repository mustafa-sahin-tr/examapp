using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.MockExams;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.TopicPerformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Coaching;

public enum CoachSaveError
{
    None,
    EmptyText,
    TooLong,
    /// <summary>Hedef çocuğa ve veliye gider: bağlantı ve görsel dahil hiçbir etiket kabul edilmez.</summary>
    MarkupNotAllowed,
    InvalidWeek,
    TooManyGoals,
    TooManyNotes,
}

public sealed record CoachSaveResult<T>(T? Value, CoachSaveError Error = CoachSaveError.None) where T : class;

/// <summary>Çağıran öğrenciye erişimi denetler (<see cref="SchoolAccessPolicyCoachExtensions"/>): bu servis öğretmenin yalnız kendi satırlarını okur.</summary>
public interface ICoachService
{
    Task<List<CoachStudentDto>> ListStudentsAsync(SchoolScope scope, CancellationToken ct = default);
    Task<CoachOverviewDto> GetOverviewAsync(int teacherUserId, int studentId, CancellationToken ct = default);

    Task<List<CoachGoalDto>> ListGoalsAsync(int teacherUserId, int studentId, DateOnly? weekStart, CancellationToken ct = default);
    Task<CoachSaveResult<CoachGoalDto>> AddGoalAsync(int teacherUserId, int studentId, SaveCoachGoalDto request, CancellationToken ct = default);
    /// <returns>null: hedef bu öğretmenin ve öğrencinin değil.</returns>
    Task<CoachSaveResult<CoachGoalDto>?> UpdateGoalAsync(int teacherUserId, int studentId, int goalId, SaveCoachGoalDto request, CancellationToken ct = default);
    Task<bool> DeleteGoalAsync(int teacherUserId, int studentId, int goalId, CancellationToken ct = default);

    Task<List<CoachNoteDto>> ListNotesAsync(int teacherUserId, int studentId, CancellationToken ct = default);
    Task<CoachSaveResult<CoachNoteDto>> AddNoteAsync(int teacherUserId, int studentId, SaveCoachNoteDto request, CancellationToken ct = default);
    /// <returns>null: not bu öğretmenin ve öğrencinin değil.</returns>
    Task<CoachSaveResult<CoachNoteDto>?> UpdateNoteAsync(int teacherUserId, int studentId, int noteId, SaveCoachNoteDto request, CancellationToken ct = default);
    Task<bool> DeleteNoteAsync(int teacherUserId, int studentId, int noteId, CancellationToken ct = default);

    Task<List<StudentCoachGoalDto>> ListGoalsForStudentAsync(int studentId, CancellationToken ct = default);
}

public interface IParentCoachSummaryService
{
    /// <returns>null: bağlı çocuk değil.</returns>
    Task<ParentCoachSummaryDto?> GetAsync(int parentUserId, int studentId, CancellationToken ct = default);
}

/// <summary>
/// Issue #700: koç paneli. Kim koç: <see cref="SchoolAccessPolicyCoachExtensions"/> (#696 kuralı). Özet başka servislerin okuma
/// kurallarını kullanır, kopyalamaz.
/// </summary>
public sealed class CoachService(
    AppDbContext db,
    ILocalDayCalendar calendar,
    IStudentService students,
    IAuthApiClient authApi,
    IMockExamService mockExams,
    ITopicPerformanceService topicPerformance,
    IPracticeSessionService practice,
    ILogger<CoachService> logger) : ICoachService
{
    internal const int MaxGoalsPerWeek = 10;
    private const int MaxFutureWeeks = 4;
    private const int MaxNotes = 100;
    internal const int MaxStoredNotes = 200;
    private const int MaxMockExams = 5;
    private const int MaxWeakTopics = 5;
    private const int WeakTopicWindowDays = 30;
    private const int WeakTopicMinQuestions = 5;
    private const decimal WeakTopicAccuracyPct = 70m;

    private static readonly TimeSpan NameLookupTimeout = TimeSpan.FromSeconds(3);

    private DateOnly CurrentWeek => ParentDashboardService.StartOfWeek(calendar.Today);

    public async Task<List<CoachStudentDto>> ListStudentsAsync(SchoolScope scope, CancellationToken ct = default)
    {
        var result = new Dictionary<int, CoachStudentDto>();
        if (scope.SchoolId.HasValue)
            Merge(await students.GetStudentLookupsAsync(scope, ct), "School");
        Merge(await students.GetStudentLookupsAsync(SchoolScope.For(scope.UserId, null), ct), "PrivateLesson");
        return result.Values.OrderBy(s => s.FullName, StringComparer.CurrentCulture).ThenBy(s => s.Id).ToList();

        void Merge(IEnumerable<StudentLookupDto> rows, string relationship)
        {
            foreach (var row in rows)
                result.TryAdd(row.Id, new CoachStudentDto { Id = row.Id, FullName = row.FullName, GradeId = row.GradeId, Relationship = relationship });
        }
    }

    public async Task<CoachOverviewDto> GetOverviewAsync(int teacherUserId, int studentId, CancellationToken ct = default)
    {
        var student = await db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            // Okul yalnız doğrulanmış üyelikte (GetStudentProfile ile aynı kural): yanlış defteri atama kapsamını buradan çözer.
            .Select(s => new { s.UserId, Profile = new StudentProfileDto { Id = s.Id, GradeId = s.GradeId, SchoolId = s.SchoolVerifiedAt != null ? s.SchoolId : null } })
            .SingleAsync(ct);

        var user = await LookupUserAsync(student.UserId, ct);
        var now = DateTime.UtcNow;

        var templateCodes = (await mockExams.ListTemplatesAsync(ct)).Select(t => t.Code).ToList();
        var mocks = await mockExams.GetLatestCompleteResultsAsync(studentId, templateCodes, ct);

        var weak = await topicPerformance.GetAsync(studentId, now.AddDays(-WeakTopicWindowDays), now, null, TopicPerformanceGroupBy.Topic, WeakTopicMinQuestions, ct);

        var mistakes = await practice.GetMistakesAsync(student.Profile, null, null, ct);

        return new CoachOverviewDto
        {
            StudentId = studentId,
            FullName = user?.FullName ?? string.Empty,
            Plan = await CoachPlanReader.ReadAsync(db, calendar, studentId, user?.KeycloakId, ct),
            RecentMockExams = mocks.OrderByDescending(m => m.Id).Take(MaxMockExams).ToList(),
            WeakTopics = weak.Items.Where(i => i.AccuracyPct < WeakTopicAccuracyPct).Take(MaxWeakTopics).ToList(),
            ActiveMistakeCount = mistakes.TotalCount,
            Goals = await ListGoalsAsync(teacherUserId, studentId, null, ct),
        };
    }

    public Task<List<CoachGoalDto>> ListGoalsAsync(int teacherUserId, int studentId, DateOnly? weekStart, CancellationToken ct = default)
    {
        var week = weekStart ?? CurrentWeek;
        return db.CoachWeeklyGoals.AsNoTracking()
            .Where(g => g.TeacherUserId == teacherUserId && g.StudentId == studentId && g.WeekStart == week)
            .OrderBy(g => g.Id)
            .Select(g => new CoachGoalDto { Id = g.Id, WeekStart = g.WeekStart, Title = g.Title, UpdatedAt = g.UpdatedAt })
            .ToListAsync(ct);
    }

    public async Task<CoachSaveResult<CoachGoalDto>> AddGoalAsync(int teacherUserId, int studentId, SaveCoachGoalDto request, CancellationToken ct = default)
    {
        var (title, titleError) = CleanTitle(request.Title);
        if (title is null)
            return new(null, titleError);

        var week = request.WeekStart ?? CurrentWeek;
        if (week.DayOfWeek != DayOfWeek.Monday || week < CurrentWeek || week > CurrentWeek.AddDays(7 * MaxFutureWeeks))
            return new(null, CoachSaveError.InvalidWeek);

        var count = await db.CoachWeeklyGoals.CountAsync(g => g.TeacherUserId == teacherUserId && g.StudentId == studentId && g.WeekStart == week, ct);
        if (count >= MaxGoalsPerWeek)
            return new(null, CoachSaveError.TooManyGoals);

        var now = DateTime.UtcNow;
        var goal = new CoachWeeklyGoal { StudentId = studentId, TeacherUserId = teacherUserId, WeekStart = week, Title = title, CreatedAt = now, UpdatedAt = now };
        db.CoachWeeklyGoals.Add(goal);
        await db.SaveChangesAsync(ct);
        return new(ToDto(goal));
    }

    public async Task<CoachSaveResult<CoachGoalDto>?> UpdateGoalAsync(int teacherUserId, int studentId, int goalId, SaveCoachGoalDto request, CancellationToken ct = default)
    {
        var goal = await db.CoachWeeklyGoals.FirstOrDefaultAsync(g => g.Id == goalId && g.TeacherUserId == teacherUserId && g.StudentId == studentId, ct);
        if (goal is null)
            return null;

        var (title, titleError) = CleanTitle(request.Title);
        if (title is null)
            return new(null, titleError);

        goal.Title = title;
        goal.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new(ToDto(goal));
    }

    public async Task<bool> DeleteGoalAsync(int teacherUserId, int studentId, int goalId, CancellationToken ct = default) =>
        await db.CoachWeeklyGoals
            .Where(g => g.Id == goalId && g.TeacherUserId == teacherUserId && g.StudentId == studentId)
            .ExecuteDeleteAsync(ct) > 0;

    public Task<List<CoachNoteDto>> ListNotesAsync(int teacherUserId, int studentId, CancellationToken ct = default) =>
        db.CoachNotes.AsNoTracking()
            .Where(n => n.TeacherUserId == teacherUserId && n.StudentId == studentId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(MaxNotes)
            .Select(n => new CoachNoteDto { Id = n.Id, Body = n.Body, CreatedAt = n.CreatedAt, UpdatedAt = n.UpdatedAt })
            .ToListAsync(ct);

    public async Task<CoachSaveResult<CoachNoteDto>> AddNoteAsync(int teacherUserId, int studentId, SaveCoachNoteDto request, CancellationToken ct = default)
    {
        var (body, bodyError) = Clean(request.Body, CoachNote.MaxStoredBodyLength);
        if (body is null)
            return new(null, bodyError);

        var count = await db.CoachNotes.CountAsync(n => n.TeacherUserId == teacherUserId && n.StudentId == studentId, ct);
        if (count >= MaxStoredNotes)
            return new(null, CoachSaveError.TooManyNotes);

        var now = DateTime.UtcNow;
        var note = new CoachNote { StudentId = studentId, TeacherUserId = teacherUserId, Body = body, CreatedAt = now, UpdatedAt = now };
        db.CoachNotes.Add(note);
        await db.SaveChangesAsync(ct);
        return new(ToDto(note));
    }

    public async Task<CoachSaveResult<CoachNoteDto>?> UpdateNoteAsync(int teacherUserId, int studentId, int noteId, SaveCoachNoteDto request, CancellationToken ct = default)
    {
        var note = await db.CoachNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.TeacherUserId == teacherUserId && n.StudentId == studentId, ct);
        if (note is null)
            return null;

        var (body, bodyError) = Clean(request.Body, CoachNote.MaxStoredBodyLength);
        if (body is null)
            return new(null, bodyError);

        note.Body = body;
        note.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new(ToDto(note));
    }

    public async Task<bool> DeleteNoteAsync(int teacherUserId, int studentId, int noteId, CancellationToken ct = default) =>
        await db.CoachNotes
            .Where(n => n.Id == noteId && n.TeacherUserId == teacherUserId && n.StudentId == studentId)
            .ExecuteDeleteAsync(ct) > 0;

    public async Task<List<StudentCoachGoalDto>> ListGoalsForStudentAsync(int studentId, CancellationToken ct = default)
    {
        var week = CurrentWeek;
        var goals = await db.CoachWeeklyGoals.AsNoTracking()
            .Where(g => g.StudentId == studentId && g.WeekStart == week)
            .OrderBy(g => g.Id)
            .Select(g => new { g.TeacherUserId, g.WeekStart, g.Title })
            .ToListAsync(ct);
        if (goals.Count == 0)
            return [];

        var names = await LookupNamesAsync(goals.Select(g => g.TeacherUserId).Distinct().ToList(), ct);
        return goals.Select(g => new StudentCoachGoalDto { WeekStart = g.WeekStart, Title = g.Title, CoachName = names.GetValueOrDefault(g.TeacherUserId) }).ToList();
    }

    /// <summary>Kaydetmeden önce temizlenir: yalnız etiketten oluşan metin boş sayılsın. Kayıtta <see cref="AppDbContext"/> yeniden temizler.</summary>
    private static (string? Value, CoachSaveError Error) Clean(string value, int maxStoredLength)
    {
        var sanitized = RichTextSanitizer.Sanitize(value.Trim());
        if (string.IsNullOrWhiteSpace(sanitized))
            return (null, CoachSaveError.EmptyText);
        return sanitized.Length > maxStoredLength ? (null, CoachSaveError.TooLong) : (sanitized, CoachSaveError.None);
    }

    /// <summary>Temizleyici düz metindeki <c>&lt;</c>'yi kodlar; kalan <c>&lt;</c> izin verilmiş bir etikettir (bağlantı, görsel).</summary>
    private static (string? Value, CoachSaveError Error) CleanTitle(string value)
    {
        var (title, error) = Clean(value, CoachWeeklyGoal.MaxStoredTitleLength);
        return title is not null && title.Contains('<') ? (null, CoachSaveError.MarkupNotAllowed) : (title, error);
    }

    private static CoachGoalDto ToDto(CoachWeeklyGoal g) => new() { Id = g.Id, WeekStart = g.WeekStart, Title = g.Title, UpdatedAt = g.UpdatedAt };

    private static CoachNoteDto ToDto(CoachNote n) => new() { Id = n.Id, Body = n.Body, CreatedAt = n.CreatedAt, UpdatedAt = n.UpdatedAt };

    private async Task<UserLookupResultDto?> LookupUserAsync(int userId, CancellationToken ct) =>
        (await LookupUsersAsync([userId], ct)).FirstOrDefault(u => u.Id == userId);

    private async Task<Dictionary<int, string>> LookupNamesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct) =>
        (await LookupUsersAsync(userIds, ct))
            .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
            .GroupBy(u => u.Id)
            .ToDictionary(g => g.Key, g => g.First().FullName);

    /// <summary>Auth API erişilemezse ekran düşmez: ad boş kalır, program sayılmaz.</summary>
    private async Task<IReadOnlyList<UserLookupResultDto>> LookupUsersAsync(IReadOnlyCollection<int> userIds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(NameLookupTimeout);
        try
        {
            return await authApi.GetUsersByIdsAsync(userIds, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "[Coach] Kullanıcılar çözülemedi ({Count} kullanıcı).", userIds.Count);
            return [];
        }
    }
}

/// <summary>Hafta planı sayıları; öğretmen özeti ve veli özeti aynı kuralı kullanır.</summary>
internal static class CoachPlanReader
{
    public static async Task<CoachPlanCompletionDto> ReadAsync(AppDbContext db, ILocalDayCalendar calendar, int studentId, string? keycloakId, CancellationToken ct)
    {
        var week = ParentDashboardService.StartOfWeek(calendar.Today);
        var from = calendar.StartOfDayUtc(week);
        var to = calendar.StartOfDayUtc(week.AddDays(7));

        // Hatırlatmanın kendisi tamamlanma bilmez: planlanan test bitmiş mi oturumundan okunur.
        var done = await db.WorksheetReminders.AsNoTracking()
            .Where(r => r.StudentId == studentId
                && r.Status != WorksheetReminderStatus.Cancelled
                && r.ScheduledFor >= from && r.ScheduledFor < to)
            .Select(r => db.TestInstances.Any(i => i.StudentId == studentId && i.WorksheetId == r.WorksheetId && i.Status == WorksheetInstanceStatus.Completed))
            .ToListAsync(ct);

        var includesProgram = !string.IsNullOrEmpty(keycloakId);
        if (includesProgram)
        {
            // Kişisel program öğrenci kimliğini değil Keycloak kimliğini tutar.
            done.AddRange(await db.UserProgramSchedules.AsNoTracking()
                .Where(s => s.UserProgram.UserId == keycloakId && s.ScheduleDate >= from && s.ScheduleDate < to)
                .Select(s => s.IsCompleted)
                .ToListAsync(ct));
            done.AddRange(await db.UserProgramStudyPageSchedules.AsNoTracking()
                .Where(s => s.UserProgram.UserId == keycloakId && s.EndDate >= from && s.EndDate < to)
                .Select(s => s.IsCompleted)
                .ToListAsync(ct));
        }

        return new CoachPlanCompletionDto { WeekStart = week, Planned = done.Count, Completed = done.Count(d => d), IncludesProgram = includesProgram };
    }
}

/// <summary>Veli özeti; sıra kapı → erişim kaydı → veri (<see cref="ParentChildAccess"/>).</summary>
public sealed class ParentCoachSummaryService(
    IParentChildAccess access,
    IParentAccessAuditLog audit,
    AppDbContext db,
    ILocalDayCalendar calendar) : IParentCoachSummaryService
{
    public async Task<ParentCoachSummaryDto?> GetAsync(int parentUserId, int studentId, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant is null)
            return null;

        // Veri okunmadan önce: okuma hata verse de erişim denemesi kayıtta kalır.
        await audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildCoachSummary, ct: ct);

        // Veli özeti yalnız "Planım"ı sayar: program için auth API'den Keycloak kimliği istenmez.
        var plan = await CoachPlanReader.ReadAsync(db, calendar, grant.StudentId, null, ct);
        var goals = await db.CoachWeeklyGoals.AsNoTracking()
            .Where(g => g.StudentId == grant.StudentId && g.WeekStart == plan.WeekStart)
            .OrderBy(g => g.Id)
            .Select(g => new ParentCoachGoalDto { Title = g.Title })
            .ToListAsync(ct);

        return new ParentCoachSummaryDto { WeekStart = plan.WeekStart, PlannedCount = plan.Planned, CompletedCount = plan.Completed, Goals = goals };
    }
}

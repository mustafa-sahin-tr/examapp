using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text.Json;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Read-model aggregation for the /test/{id} detail screen + "yanlışlarımdan test" creation.
/// All numbers are computed from WorksheetInstance / WorksheetInstanceQuestion / Question.
/// </summary>
public class WorksheetDetailService : IWorksheetDetailService
{
    private const string UnclassifiedTopicName = "Sınıflandırılmamış";
    private const string RoleTeacher = "Teacher";
    private const string RoleStudent = "Student";

    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;

    public WorksheetDetailService(AppDbContext context, IAuthApiClient authApiClient)
    {
        _context = context;
        _authApiClient = authApiClient;
    }

    /// <summary>
    /// Sahip/creator adını best-effort çözer (ExamService.ResolveCreatorNamesAsync ile aynı desen).
    /// Auth-api erişilemezse sessizce null döner — detay ekranı bu yüzden çökmemeli.
    /// </summary>
    private async Task<string?> ResolveOwnerNameAsync(int createUserId, CancellationToken ct)
    {
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(new[] { createUserId });
            return users.FirstOrDefault(u => u.Id == createUserId && !string.IsNullOrWhiteSpace(u.FullName))?.FullName;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static int ScorePercent(int correct, int total) => total > 0 ? correct * 100 / total : 0;

    private sealed class InstanceData
    {
        public int InstanceId { get; init; }
        public int StudentId { get; init; }
        public WorksheetInstanceStatus Status { get; init; }
        public DateTime StartTime { get; init; }
        public DateTime? EndTime { get; init; }
        public List<(int WorksheetQuestionId, int? SelectedAnswerId)> Answers { get; init; } = new();

        public DateTime SortKey => EndTime ?? StartTime;
    }

    public async Task<WorksheetDetailDto?> GetWorksheetDetailAsync(int worksheetId, string role, int? studentId, int userId, bool isAdmin = false, CancellationToken ct = default)
    {
        var worksheet = await _context.Worksheets
            .AsNoTracking()
            .AsSplitQuery()
            .Include(w => w.BookTest)
            .Include(w => w.WorksheetQuestions).ThenInclude(wq => wq.Question).ThenInclude(q => q.Topic)
            .Include(w => w.WorksheetQuestions).ThenInclude(wq => wq.Question)
                .ThenInclude(q => q.QuestionSubTopics).ThenInclude(qst => qst.SubTopic)
            .FirstOrDefaultAsync(w => w.Id == worksheetId, ct);

        if (worksheet == null)
            return null;

        var isStudent = role == RoleStudent && studentId.HasValue;
        var isTeacher = role == RoleTeacher;

        if (!isStudent && !isTeacher)
            return null;

        // Öğretmen yalnızca kendi worksheet'inin detayını görür; admin hepsini. Öğrenci akışı değişmez.
        // Legacy (CreateUserId null/0) kayıtlar Public* işaretli olsa bile admin dışında kimseye
        // görünmez — bu kural WorksheetAccess.CanView içinde merkezi olarak uygulanır (issue #11 AC).
        if (isTeacher && !WorksheetAccess.CanView(worksheet.CreateUserId, userId, isAdmin, worksheet.TeacherSharing, worksheet.StudentVisibility))
            return null;

        // Base worksheet bilgisi authenticated Student/Teacher'a açık (eski GET {id} ile aynı açıklık).
        // Yalnız TeacherInsights sahiplik ile gate'lenir; attempts/completedResult/rank zaten studentId ile sınırlı.
        var isWorksheetOwner = worksheet.CreateUserId.HasValue && worksheet.CreateUserId.Value > 0
            && worksheet.CreateUserId.Value == userId;

        var teacherOwnsWorksheet = isTeacher && (
            worksheet.CreateUserId == userId
            || await _context.WorksheetAssignments.AsNoTracking()
                .AnyAsync(a => a.WorksheetId == worksheetId && a.CreateUserId == userId, ct));

        // OwnerName: admin/sahip için, veya bu satır yalnızca Public* paylaşım sayesinde görünüyorsa
        // (issue #11 — CanView true ama ne owner ne admin) yine çözülür; öğrenci akışında anlamsız.
        var isSharedRow = isTeacher && !isWorksheetOwner && !isAdmin;
        string? ownerName = null;
        if ((isAdmin || isWorksheetOwner || isSharedRow) && worksheet.CreateUserId.HasValue && worksheet.CreateUserId.Value > 0)
        {
            ownerName = await ResolveOwnerNameAsync(worksheet.CreateUserId.Value, ct);
        }

        var questions = worksheet.WorksheetQuestions
            .Where(wq => wq.Question != null)
            .OrderBy(wq => wq.Order)
            .ToList();
        var totalQuestions = questions.Count;

        var correctMap = questions.ToDictionary(wq => wq.Id, wq => wq.Question.CorrectAnswerId);
        var topicMap = questions.ToDictionary(
            wq => wq.Id,
            wq => (TopicId: wq.Question.TopicId, Name: wq.Question.Topic?.Name ?? UnclassifiedTopicName));

        int CorrectOf(InstanceData i) => i.Answers.Count(a =>
            a.SelectedAnswerId != null
            && correctMap.TryGetValue(a.WorksheetQuestionId, out var ca)
            && ca != null && ca == a.SelectedAnswerId);

        // Lightweight projection: only the two columns we score on, no full entity graph.
        var instances = (await _context.TestInstances
            .AsNoTracking()
            .Where(ti => ti.WorksheetId == worksheetId)
            .Select(ti => new
            {
                ti.Id,
                ti.StudentId,
                ti.Status,
                ti.StartTime,
                ti.EndTime,
                Answers = ti.WorksheetInstanceQuestions
                    .Select(wiq => new { wiq.WorksheetQuestionId, wiq.SelectedAnswerId })
                    .ToList()
            })
            .ToListAsync(ct))
            .Select(x => new InstanceData
            {
                InstanceId = x.Id,
                StudentId = x.StudentId,
                Status = x.Status,
                StartTime = x.StartTime,
                EndTime = x.EndTime,
                Answers = x.Answers.Select(a => (a.WorksheetQuestionId, a.SelectedAnswerId)).ToList()
            })
            .ToList();

        var completedInstances = instances.Where(i => i.Status == WorksheetInstanceStatus.Completed).ToList();

        var result = new WorksheetDetailDto
        {
            Worksheet = new WorksheetDto
            {
                Id = worksheet.Id,
                Name = worksheet.Name,
                Description = worksheet.Description,
                GradeId = worksheet.GradeId,
                SubjectId = worksheet.SubjectId,
                TopicId = worksheet.TopicId,
                SubTopicId = worksheet.SubTopicId,
                MaxDurationSeconds = worksheet.MaxDurationSeconds,
                IsPracticeTest = worksheet.IsPracticeTest,
                Subtitle = worksheet.Subtitle,
                ImageUrl = worksheet.ImageUrl,
                BadgeText = worksheet.BadgeText,
                BookTestId = worksheet.BookTestId,
                BookId = worksheet.BookTest?.BookId,
                QuestionCount = totalQuestions,
                // Görünürlük + sahiplik alanları — ExamService.ApplyOwnershipAndVisibility ile aynı mantık
                // (bu servis ayrı DbContext/lifetime kullandığı için helper paylaşılmıyor).
                TeacherSharing = worksheet.TeacherSharing,
                StudentVisibility = worksheet.StudentVisibility,
                IsOwner = isWorksheetOwner,
                OwnerName = ownerName,
                CanEdit = isTeacher && WorksheetAccess.CanModify(worksheet.CreateUserId, userId, isAdmin),
                CanAssign = isTeacher && WorksheetAccess.CanAssign(worksheet.CreateUserId, userId, isAdmin, worksheet.TeacherSharing)
            },
            RewardBadgeText = worksheet.BadgeText,
            Stats = new WorksheetStatsDto
            {
                SolverCount = instances.Select(i => i.StudentId).Distinct().Count(),
                AverageScorePercent = completedInstances.Count > 0
                    ? (int)Math.Round(completedInstances.Average(i => ScorePercent(CorrectOf(i), i.Answers.Count)))
                    : (int?)null
            }
        };

        // ---- topic breakdown ----
        if (totalQuestions > 0)
        {
            result.TopicBreakdown = questions
                .GroupBy(wq => wq.Question.TopicId)
                .Select(g =>
                {
                    var count = g.Count();
                    return new WorksheetTopicBreakdownDto
                    {
                        TopicId = g.Key,
                        Name = g.First().Question.Topic?.Name ?? UnclassifiedTopicName,
                        QuestionCount = count,
                        WeightPercent = count * 100 / totalQuestions
                    };
                })
                .OrderByDescending(t => t.QuestionCount)
                .ToList();
        }

        // ---- outcomes ----
        result.Outcomes = questions
            .SelectMany(wq => wq.Question.QuestionSubTopics)
            .Where(qst => qst.SubTopic != null && !string.IsNullOrWhiteSpace(qst.SubTopic.Name))
            .Select(qst => qst.SubTopic.Name.Trim())
            .Distinct()
            .Take(6)
            .ToList();

        // ---- sample question ----
        var firstQuestion = questions.FirstOrDefault()?.Question;
        if (firstQuestion != null)
        {
            result.SampleQuestion = new WorksheetSampleQuestionDto
            {
                Id = firstQuestion.Id,
                Text = firstQuestion.Text,
                ImageUrl = firstQuestion.ImageUrl
            };
        }

        // ---- attempts (student, own completed) ----
        if (isStudent)
        {
            var mine = completedInstances
                .Where(i => i.StudentId == studentId!.Value)
                .OrderByDescending(i => i.SortKey)
                .ToList();

            result.Attempts = mine.Select(i =>
            {
                var correct = CorrectOf(i);
                var total = i.Answers.Count;
                return new WorksheetAttemptDto
                {
                    InstanceId = i.InstanceId,
                    CompletedDate = i.EndTime,
                    DurationSeconds = i.EndTime.HasValue
                        ? (int)Math.Max(0, (i.EndTime.Value - i.StartTime).TotalSeconds)
                        : 0,
                    CorrectCount = correct,
                    TotalCount = total,
                    ScorePercent = ScorePercent(correct, total)
                };
            }).ToList();

            if (result.Attempts.Count >= 2)
                result.ImprovementPoints = result.Attempts.First().ScorePercent - result.Attempts.Last().ScorePercent;
        }

        // ---- similar worksheets ----
        result.SimilarWorksheets = await BuildSimilarWorksheetsAsync(worksheet, ct);

        // ---- teacher insights (sahiplik gerekir; değilse null, 404 değil) ----
        if (teacherOwnsWorksheet)
            result.TeacherInsights = BuildTeacherInsights(questions, totalQuestions, completedInstances, correctMap);

        // ---- completed result (student) ----
        if (isStudent)
        {
            var latest = completedInstances
                .Where(i => i.StudentId == studentId!.Value)
                .OrderByDescending(i => i.SortKey)
                .FirstOrDefault();

            if (latest != null)
                result.CompletedResult = await BuildCompletedResultAsync(worksheetId, latest, correctMap, topicMap, studentId!.Value, ct);
        }

        // ---- planned reminder (student) ----
        if (isStudent)
        {
            var reminder = await _context.WorksheetReminders
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.WorksheetId == worksheetId
                    && r.StudentId == studentId!.Value
                    && r.Status != WorksheetReminderStatus.Cancelled, ct);

            if (reminder != null)
                result.PlannedReminder = WorksheetReminderService.MapToDto(reminder);
        }

        return result;
    }

    private async Task<List<SimilarWorksheetDto>> BuildSimilarWorksheetsAsync(Worksheet worksheet, CancellationToken ct)
    {
        var candidates = await _context.Worksheets
            .AsNoTracking()
            .Where(w => w.Id != worksheet.Id
                && w.GradeId == worksheet.GradeId
                && w.SubjectId == worksheet.SubjectId)
            .OrderByDescending(w => w.Id)
            .Take(5)
            .Select(w => new
            {
                w.Id,
                w.Name,
                w.IsPracticeTest,
                QuestionCount = w.WorksheetQuestions.Count()
            })
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return new List<SimilarWorksheetDto>();

        var ids = candidates.Select(c => c.Id).ToList();

        var perInstance = await _context.TestInstances
            .AsNoTracking()
            .Where(ti => ids.Contains(ti.WorksheetId) && ti.Status == WorksheetInstanceStatus.Completed)
            .Select(ti => new
            {
                ti.WorksheetId,
                Total = ti.WorksheetInstanceQuestions.Count(),
                Correct = ti.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null
                    && ti.Worksheet.WorksheetQuestions.Any(wq =>
                        wq.Id == wiq.WorksheetQuestionId
                        && wq.Question.CorrectAnswerId == wiq.SelectedAnswerId))
            })
            .ToListAsync(ct);

        var avgByWorksheet = perInstance
            .Where(x => x.Total > 0)
            .GroupBy(x => x.WorksheetId)
            .ToDictionary(g => g.Key, g => (int)Math.Round(g.Average(x => x.Correct * 100.0 / x.Total)));

        return candidates.Select(c => new SimilarWorksheetDto
        {
            Id = c.Id,
            Name = c.Name,
            QuestionCount = c.QuestionCount,
            IsPracticeTest = c.IsPracticeTest,
            AverageScorePercent = avgByWorksheet.TryGetValue(c.Id, out var avg) ? avg : (int?)null
        }).ToList();
    }

    private static WorksheetTeacherInsightsDto BuildTeacherInsights(
        List<WorksheetQuestion> questions,
        int totalQuestions,
        List<InstanceData> completedInstances,
        Dictionary<int, int?> correctMap)
    {
        var answers = completedInstances
            .SelectMany(i => i.Answers)
            .Where(a => a.SelectedAnswerId != null)
            .ToList();

        var statsByWq = answers
            .GroupBy(a => a.WorksheetQuestionId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var answered = g.Count();
                    var correct = g.Count(a =>
                        correctMap.TryGetValue(a.WorksheetQuestionId, out var ca)
                        && ca != null && ca == a.SelectedAnswerId);
                    return (Answered: answered, Correct: correct);
                });

        var hardest = questions
            .Where(wq => statsByWq.TryGetValue(wq.Id, out var s) && s.Answered > 0)
            .Select(wq =>
            {
                var s = statsByWq[wq.Id];
                var subtopicName = wq.Question.QuestionSubTopics
                    .Select(qst => qst.SubTopic?.Name)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                    ?? wq.Question.Topic?.Name;
                return new HardestQuestionDto
                {
                    QuestionId = wq.Question.Id,
                    Order = wq.Order,
                    Text = wq.Question.Text,
                    SubtopicName = subtopicName,
                    AnsweredCount = s.Answered,
                    CorrectPercent = ScorePercent(s.Correct, s.Answered)
                };
            })
            .OrderBy(h => h.CorrectPercent)
            .ThenByDescending(h => h.AnsweredCount)
            .Take(5)
            .ToList();

        var distribution = new DifficultyDistributionDto();
        foreach (var wq in questions)
        {
            var level = wq.Question.DifficultyLevel;
            if (level <= 3) distribution.Easy++;
            else if (level <= 6) distribution.Medium++;
            else distribution.Hard++;
        }

        var classified = questions.Count(wq => wq.Question.ClassificationSource != null);

        return new WorksheetTeacherInsightsDto
        {
            HardestQuestions = hardest,
            DifficultyDistribution = distribution,
            ClassifiedCount = classified,
            TotalQuestionCount = totalQuestions,
            UnclassifiedCount = totalQuestions - classified
        };
    }

    private async Task<WorksheetCompletedResultDto> BuildCompletedResultAsync(
        int worksheetId,
        InstanceData instance,
        Dictionary<int, int?> correctMap,
        Dictionary<int, (int? TopicId, string Name)> topicMap,
        int studentId,
        CancellationToken ct)
    {
        var answers = instance.Answers;
        var total = answers.Count;

        var correct = answers.Count(a =>
            a.SelectedAnswerId != null
            && correctMap.TryGetValue(a.WorksheetQuestionId, out var ca)
            && ca != null && ca == a.SelectedAnswerId);
        var empty = answers.Count(a => a.SelectedAnswerId == null);
        var wrong = total - correct - empty;

        var dto = new WorksheetCompletedResultDto
        {
            InstanceId = instance.InstanceId,
            ScorePercent = ScorePercent(correct, total),
            CorrectCount = correct,
            WrongCount = wrong,
            EmptyCount = empty,
            DurationSeconds = instance.EndTime.HasValue
                ? (int)Math.Max(0, (instance.EndTime.Value - instance.StartTime).TotalSeconds)
                : 0,
            TopicSuccess = answers
                .GroupBy(a => topicMap.TryGetValue(a.WorksheetQuestionId, out var t) ? t : (TopicId: (int?)null, Name: UnclassifiedTopicName))
                .Select(g =>
                {
                    var gTotal = g.Count();
                    var gCorrect = g.Count(a =>
                        a.SelectedAnswerId != null
                        && correctMap.TryGetValue(a.WorksheetQuestionId, out var ca)
                        && ca != null && ca == a.SelectedAnswerId);
                    return new WorksheetTopicSuccessDto
                    {
                        TopicId = g.Key.TopicId,
                        Name = g.Key.Name,
                        CorrectCount = gCorrect,
                        TotalCount = gTotal,
                        SuccessPercent = ScorePercent(gCorrect, gTotal)
                    };
                })
                .OrderByDescending(t => t.TotalCount)
                .ToList()
        };

        dto.Rank = await BuildRankAsync(worksheetId, correctMap, studentId, ct);
        return dto;
    }

    private async Task<WorksheetRankDto?> BuildRankAsync(
        int worksheetId,
        Dictionary<int, int?> correctMap,
        int studentId,
        CancellationToken ct)
    {
        var gradeId = await _context.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => s.GradeId)
            .FirstOrDefaultAsync(ct);

        var hasAssignment = await _context.WorksheetAssignments.AsNoTracking()
            .AnyAsync(a => a.WorksheetId == worksheetId
                && (a.StudentId == studentId
                    || (a.StudentId == null && a.GradeId != null && a.GradeId == gradeId)), ct);
        if (!hasAssignment)
            return null;

        var studentScoped = await _context.WorksheetAssignments.AsNoTracking()
            .Where(a => a.WorksheetId == worksheetId && a.StudentId != null)
            .Select(a => a.StudentId!.Value)
            .Distinct()
            .ToListAsync(ct);

        List<int> cohort;
        if (studentScoped.Contains(studentId))
        {
            cohort = studentScoped;
        }
        else if (gradeId.HasValue)
        {
            cohort = await _context.Students.AsNoTracking()
                .Where(s => s.GradeId == gradeId.Value)
                .Select(s => s.Id)
                .ToListAsync(ct);
        }
        else
        {
            return null;
        }

        var scored = (await _context.TestInstances
            .AsNoTracking()
            .Where(ti => ti.WorksheetId == worksheetId
                && ti.Status == WorksheetInstanceStatus.Completed
                && cohort.Contains(ti.StudentId))
            .Select(ti => new
            {
                ti.StudentId,
                ti.StartTime,
                ti.EndTime,
                Total = ti.WorksheetInstanceQuestions.Count(),
                Correct = ti.WorksheetInstanceQuestions.Count(wiq =>
                    wiq.SelectedAnswerId != null
                    && ti.Worksheet.WorksheetQuestions.Any(wq =>
                        wq.Id == wiq.WorksheetQuestionId
                        && wq.Question.CorrectAnswerId == wiq.SelectedAnswerId))
            })
            .ToListAsync(ct))
            .GroupBy(x => x.StudentId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.EndTime ?? x.StartTime).First();
                return new { StudentId = g.Key, Score = ScorePercent(latest.Correct, latest.Total) };
            })
            .ToList();

        if (scored.Count == 0)
            return null;

        var ordered = scored.OrderByDescending(x => x.Score).ToList();
        var position = ordered.FindIndex(x => x.StudentId == studentId) + 1;
        if (position <= 0)
            position = ordered.Count + 1;

        return new WorksheetRankDto
        {
            Position = position,
            TotalStudents = ordered.Count,
            ClassAveragePercent = (int)Math.Round(ordered.Average(x => x.Score))
        };
    }

    public async Task<WorksheetFromMistakesResultDto?> CreateWorksheetFromMistakesAsync(int instanceId, int studentId, int userId, CancellationToken ct = default)
    {
        var instance = await _context.TestInstances
            .Include(ti => ti.Worksheet)
            .Include(ti => ti.WorksheetInstanceQuestions).ThenInclude(wiq => wiq.WorksheetQuestion).ThenInclude(wq => wq.Question)
            .FirstOrDefaultAsync(ti => ti.Id == instanceId, ct);

        if (instance == null)
            return null;

        if (instance.StudentId != studentId)
            throw new UnauthorizedAccessException("Bu test oturumu size ait değil.");

        if (instance.Status != WorksheetInstanceStatus.Completed)
            throw new InvalidOperationException("Test tamamlanmamış.");

        var expectedName = $"{instance.Worksheet.Name} — Yanlışlarım";

        // Idempotent: aynı öğrenci + aynı kaynak için zaten üretilmiş practice worksheet varsa onu döndür.
        var existing = await _context.Worksheets.AsNoTracking()
            .Where(w => w.IsPracticeTest
                && w.Name == expectedName
                && w.GradeId == instance.Worksheet.GradeId
                && _context.WorksheetAssignments.Any(a => a.WorksheetId == w.Id && a.StudentId == studentId))
            .OrderByDescending(w => w.Id)
            .Select(w => (int?)w.Id)
            .FirstOrDefaultAsync(ct);

        if (existing.HasValue)
            return new WorksheetFromMistakesResultDto { WorksheetId = existing.Value };

        // Sadece yanlış İŞARETLENEN sorular (selected != correct). Boş bırakılanlar dahil DEĞİL.
        var wrongQuestions = instance.WorksheetInstanceQuestions
            .Where(wiq => wiq.SelectedAnswerId != null
                && wiq.WorksheetQuestion?.Question != null
                && wiq.WorksheetQuestion.Question.CorrectAnswerId != wiq.SelectedAnswerId)
            .OrderBy(wiq => wiq.WorksheetQuestion.Order)
            .Select(wiq => wiq.WorksheetQuestion.Question)
            .GroupBy(q => q.Id)
            .Select(g => g.First())
            .ToList();

        if (wrongQuestions.Count == 0)
            throw new InvalidOperationException("Yanlış soru yok.");

        var source = instance.Worksheet;
        var order = 1;
        var newWorksheet = new Worksheet
        {
            Name = expectedName,
            Description = source.Description,
            GradeId = source.GradeId,
            SubjectId = source.SubjectId,
            TopicId = source.TopicId,
            SubTopicId = source.SubTopicId,
            MaxDurationSeconds = source.MaxDurationSeconds,
            IsPracticeTest = true,
            WorksheetQuestions = wrongQuestions
                .Select(q => new WorksheetQuestion { QuestionId = q.Id, Order = order++ })
                .ToList()
        };

        // Öğrenciye özel atama: yalnız bu öğrencinin listesinde çıksın (sınıf geneline sızmasın).
        var assignment = new WorksheetAssignment
        {
            Worksheet = newWorksheet,
            StudentId = studentId,
            StartAt = DateTime.UtcNow
        };

        _context.SetCurrentUser(userId);
        _context.Worksheets.Add(newWorksheet);
        _context.WorksheetAssignments.Add(assignment);
        await _context.SaveChangesAsync(ct);

        return new WorksheetFromMistakesResultDto { WorksheetId = newWorksheet.Id };
    }
}

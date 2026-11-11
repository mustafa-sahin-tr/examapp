using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Practice;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Placement;

public enum PlacementError
{
    None,
    /// <summary>Sınıf yok, öğrencinin sınıfı yok ya da yayımlanmış test yok → 404.</summary>
    NotFound,
    /// <summary>Sınıfın sınav derslerinde bankada yeterli çoktan seçmeli soru yok → 409.</summary>
    NotEnoughQuestions,
    /// <summary>Öğrenci bu sınıfın testini çözdü → 409.</summary>
    AlreadyTaken,
    /// <summary>Teste ait olmayan soru ya da şık → 400.</summary>
    InvalidAnswers,
}

public sealed record PlacementResult<T>(PlacementError Error, T? Value = default);

public interface IPlacementService
{
    Task<List<AdminPlacementTestDto>> ListAsync(CancellationToken ct = default);
    Task<PlacementResult<AdminPlacementTestDto>> GenerateAsync(int gradeId, CancellationToken ct = default);
    Task<PlacementResult<AdminPlacementTestDto>> SetPublishedAsync(int gradeId, bool published, CancellationToken ct = default);

    Task<PlacementStatusDto> StatusAsync(int studentUserId, CancellationToken ct = default);
    Task<PlacementResult<List<PlacementQuestionDto>>> QuestionsAsync(int studentUserId, CancellationToken ct = default);
    Task<PlacementResult<PlacementReportDto>> SubmitAsync(int studentUserId, PlacementSubmitDto request, CancellationToken ct = default);
    Task<PlacementReportDto?> ReportAsync(int studentUserId, CancellationToken ct = default);

    /// <summary>Veli: çocuğun bu sınıftaki raporu. Bağlantı yoksa <c>Found=false</c>; rapor yoksa <c>Report=null</c>.</summary>
    Task<(bool Found, PlacementReportDto? Report)> ReportForParentAsync(int parentUserId, int studentId, CancellationToken ct = default);
}

/// <summary>
/// Issue #1114 (epic #1118): ücretsiz seviye tespiti. Admin sınıf başına testi bankadan üretir: sınıfın sınav dersleri
/// (<see cref="StudentPlanPricing.SingleSubjectCodes"/>) arasında dengeli, ders içinde konulara dağıtılmış, yalnız çoktan seçmeli
/// (<see cref="PracticeQuestionPool"/>) hedef <see cref="TargetQuestions"/> soru (en çok <see cref="MaxQuestions"/>). Öğrenci sınıfı
/// başına bir kez çözer; cevaplar tek gönderimde puanlanır, rapor ve plan önerisi saklanır, veliyle paylaşılır.
/// </summary>
public sealed class PlacementService(AppDbContext db, IParentChildAccess access, IParentAccessAuditLog audit, TimeProvider clock)
    : IPlacementService
{
    internal const int TargetQuestions = 24;
    internal const int MaxQuestions = 30;
    internal const int MinQuestions = 10;
    internal const int MinPerSubject = 3;
    internal const int MaxPerSubject = 10;
    internal const int MaxTopics = 5;
    /// <summary>Ders bu yüzdenin altındaysa zayıf sayılır (tek ders önerisi).</summary>
    internal const int WeakSubjectPercent = 60;
    /// <summary>Genel başarı bunun altındaysa Pro, <see cref="EssentialBelowPercent"/> altındaysa Essential, değilse Basic önerilir.</summary>
    internal const int ProBelowPercent = 50;
    internal const int EssentialBelowPercent = 75;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record QuestionFacts(int Id, int? SubjectId, string? SubjectName, int? TopicId, string? TopicName, int? CorrectAnswerId,
        List<int> AnswerIds);

    // ---- Admin ------------------------------------------------------------------------------------------------

    public async Task<List<AdminPlacementTestDto>> ListAsync(CancellationToken ct = default)
    {
        var tests = await db.PlacementTests.AsNoTracking()
            .Select(t => new
            {
                t.GradeId,
                t.GeneratedAtUtc,
                t.PublishedAtUtc,
                QuestionCount = t.Questions.Count,
                AttemptCount = db.PlacementAttempts.Count(a => a.PlacementTestId == t.Id),
            })
            .ToDictionaryAsync(t => t.GradeId, ct);
        var grades = await db.Grades.AsNoTracking().OrderBy(g => g.Id).Select(g => new { g.Id, g.Name }).ToListAsync(ct);
        return grades.Select(g => tests.TryGetValue(g.Id, out var t)
                ? new AdminPlacementTestDto
                {
                    GradeId = g.Id, GradeName = g.Name, QuestionCount = t.QuestionCount, GeneratedAtUtc = t.GeneratedAtUtc,
                    PublishedAtUtc = t.PublishedAtUtc, AttemptCount = t.AttemptCount,
                }
                : new AdminPlacementTestDto { GradeId = g.Id, GradeName = g.Name })
            .ToList();
    }

    public async Task<PlacementResult<AdminPlacementTestDto>> GenerateAsync(int gradeId, CancellationToken ct = default)
    {
        if (!await db.Grades.AnyAsync(g => g.Id == gradeId, ct))
            return new(PlacementError.NotFound);

        var subjectIds = await StudentPlanPricing.SingleSubjectsOf(db, gradeId).Select(gs => gs.SubjectId).Distinct().ToListAsync(ct);
        var picked = new List<int>();
        if (subjectIds.Count > 0)
        {
            var perSubject = Math.Clamp((int)Math.Ceiling(TargetQuestions / (double)subjectIds.Count), MinPerSubject, MaxPerSubject);
            foreach (var subjectId in subjectIds.Order())
            {
                var pool = await PracticeQuestionPool.Build(db, gradeId, [subjectId], [])
                    .Where(q => q.Answers.Count >= 2)
                    .Select(q => new { q.Id, q.TopicId })
                    .ToListAsync(ct);
                // Konulara dağıt: her turda her konudan bir soru.
                var byTopic = pool.OrderBy(_ => Random.Shared.Next()).GroupBy(q => q.TopicId).Select(g => new Queue<int>(g.Select(q => q.Id))).ToList();
                var taken = new List<int>();
                while (taken.Count < perSubject && byTopic.Any(q => q.Count > 0))
                {
                    foreach (var queue in byTopic.Where(q => q.Count > 0))
                    {
                        if (taken.Count >= perSubject)
                            break;
                        taken.Add(queue.Dequeue());
                    }
                }
                picked.AddRange(taken);
            }
        }
        if (picked.Count < MinQuestions)
            return new(PlacementError.NotEnoughQuestions);
        picked = picked.Take(MaxQuestions).ToList();

        var test = await db.PlacementTests.Include(t => t.Questions).FirstOrDefaultAsync(t => t.GradeId == gradeId, ct);
        if (test is null)
        {
            test = new PlacementTest { GradeId = gradeId };
            db.PlacementTests.Add(test);
        }
        test.Questions.Clear();
        test.Questions.AddRange(picked.Select((id, i) => new PlacementTestQuestion { QuestionId = id, Order = i + 1 }));
        test.GeneratedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return new(PlacementError.None, await AdminDetailAsync(gradeId, ct));
    }

    public async Task<PlacementResult<AdminPlacementTestDto>> SetPublishedAsync(int gradeId, bool published, CancellationToken ct = default)
    {
        var test = await db.PlacementTests.FirstOrDefaultAsync(t => t.GradeId == gradeId, ct);
        if (test is null)
            return new(PlacementError.NotFound);
        test.PublishedAtUtc = published ? test.PublishedAtUtc ?? clock.GetUtcNow().UtcDateTime : null;
        await db.SaveChangesAsync(ct);
        return new(PlacementError.None, await AdminDetailAsync(gradeId, ct));
    }

    private async Task<AdminPlacementTestDto> AdminDetailAsync(int gradeId, CancellationToken ct)
    {
        var dto = (await ListAsync(ct)).First(t => t.GradeId == gradeId);
        dto.Subjects = await db.PlacementTestQuestions.AsNoTracking()
            .Where(q => q.PlacementTest.GradeId == gradeId)
            .GroupBy(q => new { q.Question.SubjectId, Name = q.Question.Subject != null ? q.Question.Subject.Name : null })
            .Select(g => new PlacementSubjectScoreDto { SubjectId = g.Key.SubjectId ?? 0, SubjectName = g.Key.Name ?? string.Empty, Total = g.Count() })
            .ToListAsync(ct);
        return dto;
    }

    // ---- Öğrenci ----------------------------------------------------------------------------------------------

    public async Task<PlacementStatusDto> StatusAsync(int studentUserId, CancellationToken ct = default)
    {
        var student = await StudentAsync(studentUserId, ct);
        if (student?.GradeId is not { } gradeId)
            return new PlacementStatusDto();
        var test = await PublishedTestAsync(gradeId, ct);
        return new PlacementStatusDto
        {
            Available = test is not null,
            Taken = await db.PlacementAttempts.AnyAsync(a => a.StudentId == student.Id && a.GradeId == gradeId, ct),
            GradeName = student.GradeName,
            QuestionCount = test?.QuestionCount ?? 0,
        };
    }

    public async Task<PlacementResult<List<PlacementQuestionDto>>> QuestionsAsync(int studentUserId, CancellationToken ct = default)
    {
        var student = await StudentAsync(studentUserId, ct);
        if (student?.GradeId is not { } gradeId || await PublishedTestAsync(gradeId, ct) is not { } test)
            return new(PlacementError.NotFound);
        if (await db.PlacementAttempts.AnyAsync(a => a.StudentId == student.Id && a.GradeId == gradeId, ct))
            return new(PlacementError.AlreadyTaken);

        var questions = await db.PlacementTestQuestions.AsNoTracking()
            .Where(q => q.PlacementTestId == test.Id)
            .OrderBy(q => q.Order)
            .Select(q => new PlacementQuestionDto
            {
                Id = q.QuestionId,
                SubjectName = q.Question.Subject != null ? q.Question.Subject.Name : null,
                Text = q.Question.Text,
                ImageUrl = q.Question.ImageUrl,
                Options = q.Question.Answers.OrderBy(a => a.Order ?? int.MaxValue).ThenBy(a => a.Id)
                    .Select(a => new PlacementAnswerOptionDto { Id = a.Id, Text = a.Text, ImageUrl = a.ImageUrl }).ToList(),
            })
            .ToListAsync(ct);
        return new(PlacementError.None, questions);
    }

    public async Task<PlacementResult<PlacementReportDto>> SubmitAsync(int studentUserId, PlacementSubmitDto request, CancellationToken ct = default)
    {
        var student = await StudentAsync(studentUserId, ct);
        if (student?.GradeId is not { } gradeId || await PublishedTestAsync(gradeId, ct) is not { } test)
            return new(PlacementError.NotFound);
        if (await db.PlacementAttempts.AnyAsync(a => a.StudentId == student.Id && a.GradeId == gradeId, ct))
            return new(PlacementError.AlreadyTaken);

        var facts = await db.PlacementTestQuestions.AsNoTracking()
            .Where(q => q.PlacementTestId == test.Id)
            .OrderBy(q => q.Order)
            .Select(q => new QuestionFacts(q.QuestionId, q.Question.SubjectId, q.Question.Subject != null ? q.Question.Subject.Name : null,
                q.Question.TopicId, q.Question.Topic != null ? q.Question.Topic.Name : null, q.Question.CorrectAnswerId,
                q.Question.Answers.Select(a => a.Id).ToList()))
            .ToListAsync(ct);
        var byId = facts.ToDictionary(f => f.Id);
        var answers = new Dictionary<int, int?>();
        foreach (var answer in request.Answers ?? [])
        {
            if (!byId.TryGetValue(answer.QuestionId, out var fact) || answers.ContainsKey(answer.QuestionId)
                || (answer.AnswerId is { } id && !fact.AnswerIds.Contains(id)))
                return new(PlacementError.InvalidAnswers);
            answers[answer.QuestionId] = answer.AnswerId;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var report = await BuildReportAsync(facts, answers, student, now, ct);
        db.PlacementAttempts.Add(new PlacementAttempt
        {
            StudentId = student.Id,
            GradeId = gradeId,
            PlacementTestId = test.Id,
            CorrectCount = report.Correct,
            TotalCount = report.Total,
            AnswersJson = JsonSerializer.Serialize(facts.Select(f => new PlacementAnswerDto { QuestionId = f.Id, AnswerId = answers.GetValueOrDefault(f.Id) }), Json),
            ReportJson = JsonSerializer.Serialize(report, Json),
            SubmittedAtUtc = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Çift gönderim: ilk kayıt kalır.
            db.ChangeTracker.Clear();
            return new(PlacementError.AlreadyTaken);
        }
        return new(PlacementError.None, report);
    }

    public async Task<PlacementReportDto?> ReportAsync(int studentUserId, CancellationToken ct = default)
    {
        var student = await StudentAsync(studentUserId, ct);
        return student?.GradeId is { } gradeId ? await StoredReportAsync(student.Id, gradeId, ct) : null;
    }

    public async Task<(bool Found, PlacementReportDto? Report)> ReportForParentAsync(int parentUserId, int studentId, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant is null)
            return (false, null);
        await audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildPlacement, ct: ct);
        return (true, grant.GradeId is { } gradeId ? await StoredReportAsync(grant.StudentId, gradeId, ct) : null);
    }

    private async Task<PlacementReportDto?> StoredReportAsync(int studentId, int gradeId, CancellationToken ct)
    {
        var json = await db.PlacementAttempts.AsNoTracking()
            .Where(a => a.StudentId == studentId && a.GradeId == gradeId).Select(a => a.ReportJson).FirstOrDefaultAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<PlacementReportDto>(json, Json);
    }

    private sealed record StudentRow(int Id, int UserId, int? GradeId, string? GradeName, string? GradeCode);

    private Task<StudentRow?> StudentAsync(int userId, CancellationToken ct) =>
        db.Students.AsNoTracking().Where(s => s.UserId == userId)
            .Select(s => new StudentRow(s.Id, s.UserId, s.GradeId, s.Grade != null ? s.Grade.Name : null, s.Grade != null ? s.Grade.Code : null))
            .FirstOrDefaultAsync(ct);

    private sealed record PublishedTest(int Id, int QuestionCount);

    private Task<PublishedTest?> PublishedTestAsync(int gradeId, CancellationToken ct) =>
        db.PlacementTests.AsNoTracking()
            .Where(t => t.GradeId == gradeId && t.PublishedAtUtc != null && t.Questions.Count > 0)
            .Select(t => new PublishedTest(t.Id, t.Questions.Count))
            .FirstOrDefaultAsync(ct);

    private async Task<PlacementReportDto> BuildReportAsync(List<QuestionFacts> facts, Dictionary<int, int?> answers, StudentRow student,
        DateTime now, CancellationToken ct)
    {
        bool Correct(QuestionFacts f) => answers.GetValueOrDefault(f.Id) is { } a && a == f.CorrectAnswerId;

        var subjects = facts.Where(f => f.SubjectId is not null)
            .GroupBy(f => (Id: f.SubjectId!.Value, Name: f.SubjectName ?? string.Empty))
            .Select(g => Score(g.Key.Id, g.Key.Name, g.Count(Correct), g.Count()))
            .OrderBy(s => s.SubjectName, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), false))
            .ToList();
        var topics = facts.Where(f => f.TopicId is not null)
            .GroupBy(f => (Id: f.TopicId!.Value, Name: f.TopicName ?? string.Empty, f.SubjectName))
            .Select(g => new PlacementTopicDto { TopicId = g.Key.Id, TopicName = g.Key.Name, SubjectName = g.Key.SubjectName, Correct = g.Count(Correct), Total = g.Count() })
            .ToList();

        var correct = facts.Count(Correct);
        var percent = Percent(correct, facts.Count);
        return new PlacementReportDto
        {
            GradeName = student.GradeName,
            SubmittedAtUtc = now,
            Correct = correct,
            Total = facts.Count,
            Percent = percent,
            Subjects = subjects,
            StrongTopics = topics.Where(t => t.Correct == t.Total).OrderByDescending(t => t.Total).Take(MaxTopics).ToList(),
            WeakTopics = topics.Where(t => t.Correct * 2 < t.Total || (t.Total == 1 && t.Correct == 0))
                .OrderByDescending(t => t.Total - t.Correct).Take(MaxTopics).ToList(),
            Suggestion = await SuggestAsync(percent, subjects, student, ct),
        };
    }

    /// <summary>
    /// Kademe genel başarıdan; kapsam: Pro dışında tek zayıf ders (o da tek ders satılıyorsa) varsa o ders, yoksa bütün dersler.
    /// </summary>
    private async Task<PlacementSuggestionDto> SuggestAsync(int percent, List<PlacementSubjectScoreDto> subjects, StudentRow student,
        CancellationToken ct)
    {
        var (tier, key) = percent < ProBelowPercent ? ("pro", SubscriptionPlan.StudentProKey)
            : percent < EssentialBelowPercent ? ("essential", SubscriptionPlan.StudentEssentialKey)
            : ("basic", SubscriptionPlan.StudentBasicKey);
        var suggestion = new PlacementSuggestionDto { Tier = tier, PlanKey = key };

        var weak = subjects.Where(s => s.Percent < WeakSubjectPercent).ToList();
        if (tier != "pro" && weak.Count == 1
            && await StudentPlanPricing.SingleSubjectsOf(db, student.GradeId).AnyAsync(gs => gs.SubjectId == weak[0].SubjectId, ct))
        {
            suggestion.SubjectId = weak[0].SubjectId;
            suggestion.SubjectName = weak[0].SubjectName;
        }

        var (_, level) = await StudentPlanPricing.GradeOfAsync(db, student.UserId, ct);
        suggestion.Band = StudentPlanPricing.BandOf(level)?.ToString();
        return suggestion;
    }

    private static PlacementSubjectScoreDto Score(int id, string name, int correct, int total) =>
        new() { SubjectId = id, SubjectName = name, Correct = correct, Total = total, Percent = Percent(correct, total) };

    private static int Percent(int correct, int total) => total == 0 ? 0 : (int)Math.Round(correct * 100.0 / total, MidpointRounding.AwayFromZero);
}

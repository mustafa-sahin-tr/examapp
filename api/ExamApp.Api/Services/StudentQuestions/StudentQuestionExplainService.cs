using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.TopicPerformance;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.StudentQuestions;

public enum ExplainContextStatus { Ok, NotAnswered, InOpenTest }

public sealed record ExplainImageDto(string MediaType, string Base64, int Width, int Height);

/// <summary>Issue #510: what the assistant may say about a question the student already answered.</summary>
public sealed record ExplainContextDto(
    int QuestionId,
    string Source,
    DateTime AnsweredAt,
    string Outcome,
    string? SubjectName,
    string? TopicName,
    List<string> SubTopicNames,
    string? InteractionType,
    int ChoiceCount,
    string? StudentChoice,
    string? CorrectChoice,
    string? Text,
    ExplainImageDto? Image);

public sealed record ExplainContextResult(ExplainContextStatus Status, ExplainContextDto? Context = null);

public sealed record StudentMistakeDto(
    int QuestionId,
    string? SubjectName,
    string? TopicName,
    List<string> SubTopicNames,
    DateTime LastWrongAt,
    int WrongCount,
    bool AnsweredCorrectlyLater);

public sealed record StudentMistakesDto(int Days, List<StudentMistakeDto> Items);

public interface IStudentQuestionExplainService
{
    Task<ExplainContextResult> GetExplainContextAsync(int studentId, int? gradeId, int? schoolId, int questionId, CancellationToken ct = default);

    Task<StudentMistakesDto> GetMistakesAsync(int studentId, int days, int? subjectId, int? topicId, int take, CancellationToken ct = default);
}

/// <summary>
/// Issue #510 (#495 Phase 2): context for explaining a question the student has already answered, and their recent mistakes.
/// Decision (2026-11-24): explanations only for answered questions (a finished test, or an answered practice question); no
/// hints for unanswered ones. Exam integrity: a question that is in one of the student's tests still running, or in a test
/// assigned to them that they have not finished, is refused even if they answered it in practice before.
/// The caller checks the student's identity (token only).
/// </summary>
public sealed class StudentQuestionExplainService(
    AppDbContext db,
    IQuestionClassificationService classification,
    IMinIoService storage,
    ILogger<StudentQuestionExplainService> logger) : IStudentQuestionExplainService
{
    /// <summary>Long edge of the image sent to the model (the labeling <c>MaxImageEdge</c>, Claude's recommended limit).</summary>
    public const int MaxImageEdge = 1568;

    public const int MaxDays = 90;
    public const int MaxTake = 20;

    private sealed record Attempt(string Source, DateTime At, int? SelectedAnswerId, bool HasPayload, bool StoredIsCorrect);

    public async Task<ExplainContextResult> GetExplainContextAsync(int studentId, int? gradeId, int? schoolId, int questionId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (await IsInOpenTestAsync(studentId, gradeId, schoolId, questionId, now, ct))
            return new ExplainContextResult(ExplainContextStatus.InOpenTest);

        var attempt = await LatestAttemptAsync(studentId, questionId, now, ct);
        if (attempt is null)
            return new ExplainContextResult(ExplainContextStatus.NotAnswered);

        // Deleted questions are read too: a mistake from a finished test is still explainable.
        var question = await db.Questions.AsNoTracking().IgnoreQueryFilters()
            .Where(q => q.Id == questionId)
            .Select(q => new
            {
                q.Id,
                q.IsDeleted,
                q.ImageUrl,
                q.InteractionType,
                q.CorrectAnswerId,
                SubjectName = q.Subject != null ? q.Subject.Name : null,
                TopicName = q.Topic != null ? q.Topic.Name : null,
                SubTopics = q.QuestionSubTopics.Where(s => !s.IsDeleted).Select(s => s.SubTopic.Name).ToList(),
                Answers = q.Answers.Where(a => !a.IsDeleted).OrderBy(a => a.Order).ThenBy(a => a.Id).Select(a => a.Id).ToList(),
            })
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);
        if (question is null)
            return new ExplainContextResult(ExplainContextStatus.NotAnswered);

        var outcome = WorksheetScoring.IsBlank(attempt.SelectedAnswerId, attempt.HasPayload) ? StudentAnswerOutcome.Blank
            : WorksheetScoring.IsCorrect(attempt.SelectedAnswerId, question.CorrectAnswerId, attempt.HasPayload, attempt.StoredIsCorrect,
                questionKnown: !question.IsDeleted) ? StudentAnswerOutcome.Correct
            : StudentAnswerOutcome.Wrong;

        var context = new ExplainContextDto(
            question.Id,
            attempt.Source,
            attempt.At,
            outcome.ToString(),
            question.SubjectName,
            question.TopicName,
            question.SubTopics.Distinct().ToList(),
            question.InteractionType,
            question.Answers.Count,
            Letter(question.Answers, attempt.SelectedAnswerId),
            Letter(question.Answers, question.CorrectAnswerId),
            await classification.GetClassificationTextAsync(question.Id, ct),
            await LoadImageAsync(question.Id, question.ImageUrl));
        return new ExplainContextResult(ExplainContextStatus.Ok, context);
    }

    public async Task<StudentMistakesDto> GetMistakesAsync(int studentId, int days, int? subjectId, int? topicId, int take,
        CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, MaxDays);
        take = Math.Clamp(take, 1, MaxTake);
        var to = DateTime.UtcNow;
        // Same answers as the topic report (#497): finished tests and practice; a running test is never read.
        var answers = await new StudentAnswerReader(db).ReadAsync(studentId, to.AddDays(-days), to, ct);

        var mistakes = answers
            .Where(a => subjectId is null || a.Question.SubjectId == subjectId)
            .Where(a => topicId is null || a.Question.EffectiveTopicId == topicId)
            .GroupBy(a => a.Question.Id)
            .Select(g =>
            {
                var wrong = g.Where(a => a.Outcome == StudentAnswerOutcome.Wrong).ToList();
                return wrong.Count == 0 ? null : new
                {
                    Question = g.First().Question,
                    LastWrongAt = wrong.Max(a => a.At),
                    WrongCount = wrong.Count,
                    CorrectLater = g.Any(a => a.Outcome == StudentAnswerOutcome.Correct && a.At > wrong.Max(w => w.At)),
                };
            })
            .Where(m => m is not null)
            .Select(m => m!)
            .OrderByDescending(m => m.LastWrongAt)
            .Take(take)
            .ToList();

        var subjectIds = mistakes.Select(m => m.Question.SubjectId).OfType<int>().Distinct().ToList();
        var topicIds = mistakes.Select(m => m.Question.EffectiveTopicId).OfType<int>().Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var topics = await db.Topics.AsNoTracking().Where(t => topicIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var items = mistakes.Select(m => new StudentMistakeDto(
            m.Question.Id,
            m.Question.SubjectId is { } s ? subjects.GetValueOrDefault(s) : null,
            m.Question.EffectiveTopicId is { } t ? topics.GetValueOrDefault(t) : null,
            m.Question.SubTopics.Select(st => st.Name).Distinct().ToList(),
            m.LastWrongAt,
            m.WrongCount,
            m.CorrectLater)).ToList();
        return new StudentMistakesDto(days, items);
    }

    /// <summary>
    /// The question is in a test the student is solving now, or in a test assigned to them (assignment still open) that they
    /// have not finished. Activities are not tests (no score) and are left out.
    /// </summary>
    private async Task<bool> IsInOpenTestAsync(int studentId, int? gradeId, int? schoolId, int questionId, DateTime now, CancellationToken ct)
    {
        var worksheetIds = await db.TestQuestions.AsNoTracking().WithRemovedWorksheetItems()
            .Where(tq => tq.QuestionId == questionId)
            .Where(tq => db.Worksheets.IgnoreQueryFilters().Any(w => w.Id == tq.TestId && w.Kind != WorksheetKind.Activity))
            .Select(tq => tq.TestId)
            .Distinct()
            .ToListAsync(ct);
        if (worksheetIds.Count == 0)
            return false;

        var instances = await db.TestInstances.AsNoTracking()
            .Where(i => i.StudentId == studentId && worksheetIds.Contains(i.WorksheetId))
            .Select(i => new { i.WorksheetId, i.Status, i.StartTime, i.MaxDurationSeconds })
            .ToListAsync(ct);
        var running = instances.Any(i => TestTimeLimit.EffectiveStatus(i.Status, i.StartTime, i.MaxDurationSeconds, now) == WorksheetInstanceStatus.Started);
        if (running)
            return true;

        var finishedWorksheets = instances
            .Where(i => TestTimeLimit.EffectiveStatus(i.Status, i.StartTime, i.MaxDurationSeconds, now) != WorksheetInstanceStatus.Started)
            .Select(i => i.WorksheetId)
            .ToHashSet();
        var unfinished = worksheetIds.Where(id => !finishedWorksheets.Contains(id)).ToList();
        if (unfinished.Count == 0)
            return false;

        return await db.WorksheetAssignments.AsNoTracking()
            .Where(a => unfinished.Contains(a.WorksheetId))
            .Where(a => a.EndAt == null || a.EndAt > now)
            .Where(WorksheetStudentAccess.AssignmentVisibleTo(studentId, gradeId, schoolId))
            .AnyAsync(ct);
    }

    /// <summary>The student's latest answer: an answered (not skipped) practice question, or the question in a finished test.</summary>
    private async Task<Attempt?> LatestAttemptAsync(int studentId, int questionId, DateTime now, CancellationToken ct)
    {
        var practice = await db.PracticeSessionQuestions.AsNoTracking()
            .Where(r => r.PracticeSession.StudentId == studentId && r.QuestionId == questionId && r.AnsweredAt != null && !r.IsSkipped)
            .OrderByDescending(r => r.AnsweredAt)
            .Select(r => new Attempt("practice", r.AnsweredAt!.Value, r.SelectedAnswerId, false, r.IsCorrect))
            .FirstOrDefaultAsync(ct);

        // Session rows (#684): an item removed from the worksheet afterwards still counts.
        var testRows = await db.TestInstanceQuestions.AsNoTracking().WithRemovedWorksheetItems()
            .Where(r => r.WorksheetInstance.StudentId == studentId && r.WorksheetQuestion.QuestionId == questionId)
            .Where(r => !db.Worksheets.IgnoreQueryFilters().Any(w => w.Id == r.WorksheetInstance.WorksheetId && w.Kind == WorksheetKind.Activity))
            .Select(r => new
            {
                r.WorksheetInstance.Status,
                r.WorksheetInstance.StartTime,
                r.WorksheetInstance.EndTime,
                r.WorksheetInstance.MaxDurationSeconds,
                r.SelectedAnswerId,
                HasPayload = r.AnswerPayload != null,
                r.IsCorrect,
            })
            .ToListAsync(ct);
        var test = testRows
            .Where(r => TestTimeLimit.EffectiveStatus(r.Status, r.StartTime, r.MaxDurationSeconds, now) != WorksheetInstanceStatus.Started)
            .Select(r => new Attempt("test", r.EndTime ?? TestTimeLimit.EndsAt(r.StartTime, r.MaxDurationSeconds) ?? r.StartTime,
                r.SelectedAnswerId, r.HasPayload, r.IsCorrect))
            .MaxBy(a => a.At);

        return new[] { practice, test }.OfType<Attempt>().MaxBy(a => a.At);
    }

    private static string? Letter(List<int> answerIds, int? answerId)
    {
        if (answerId is null)
            return null;
        var index = answerIds.IndexOf(answerId.Value);
        return index is >= 0 and < 26 ? ((char)('A' + index)).ToString() : null;
    }

    /// <summary>The question image as a JPEG whose long edge is at most <see cref="MaxImageEdge"/>; null when there is none.</summary>
    private async Task<ExplainImageDto?> LoadImageAsync(int questionId, string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;
        try
        {
            await using var stream = await storage.GetFileStreamAsync(imageUrl);
            if (stream is null)
                return null;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            using var image = LabelingImage.Decode(buffer.ToArray());
            var encoded = image.Encode(MaxImageEdge);
            return new ExplainImageDto("image/jpeg", Convert.ToBase64String(encoded.Jpeg), encoded.Width, encoded.Height);
        }
        catch (Exception ex) when (ex is LabelingValidationException or IOException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Explain context: image of question {QuestionId} could not be read", questionId);
            return null;
        }
    }
}

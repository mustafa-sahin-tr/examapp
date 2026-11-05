using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.TopicPerformance;

public enum StudentAnswerOutcome { Correct, Wrong, Blank }

public sealed record AnsweredSubTopic(int Id, string Name, int TopicId);

public sealed record AnsweredQuestion(int Id, bool Deleted, int? SubjectId, int? TopicId, int? CorrectAnswerId, int Difficulty, string? InteractionType, List<AnsweredSubTopic> SubTopics)
{
    /// <summary>Sorunun konusu; konusu boşsa ilk alt konusunun konusu.</summary>
    public int? EffectiveTopicId => TopicId ?? SubTopics.OrderBy(s => s.Id).Select(s => (int?)s.TopicId).FirstOrDefault();
}

public sealed record StudentAnswer(AnsweredQuestion Question, DateTime At, int TimeTaken, StudentAnswerOutcome Outcome);

/// <summary>
/// Issue #497, #696: konu performansı ve kazanım seviyesinin okuduğu tek cevap listesi; iki rapor aynı cevapları sayar.
/// Bitmiş test oturumları ve pratik cevapları. Doğru, yanlış, boş kuralı <see cref="WorksheetScoring"/>: sonuç ekranıyla aynı.
/// Süren test oturumu hiç okunmaz. Etkinlik puansızdır, açık uçlu soru puanlanmaz; ikisi de dışarıda. Sonradan silinen test ya da
/// madde cevabı düşürmez. Silinen sorunun test cevabı yanlış sayılır (sonuç ekranıyla aynı), pratik cevabı kayıtlı sonucuyla kalır.
/// Öğrenci sıfırlamasından önceki cevaplar okunmaz; sıfırlama pratik satırlarını silmediği için tarihle ayrılır.
/// Çağıran, öğrenciye erişim hakkını kendi denetler.
/// </summary>
public sealed class StudentAnswerReader(AppDbContext db)
{
    /// <summary>Cevabın kendisi; sonucu sorunun doğru şıkkı okunduktan sonra hesaplanır.</summary>
    private sealed record RawAnswer(int QuestionId, DateTime At, int TimeTaken, Func<AnsweredQuestion, StudentAnswerOutcome> Grade);

    public async Task<List<StudentAnswer>> ReadAsync(int studentId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var resetAt = await db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => s.ProgressResetAtUtc)
            .FirstOrDefaultAsync(ct);
        var earliest = resetAt > from ? resetAt.Value : from;

        var answers = await ReadTestAnswersAsync(studentId, earliest, to, ct);
        answers.AddRange(await ReadPracticeAnswersAsync(studentId, earliest, to, ct));

        var questions = await ReadQuestionsAsync(answers.Select(a => a.QuestionId).Distinct().ToList(), ct);
        return answers
            .Where(a => questions.ContainsKey(a.QuestionId))
            .Select(a => (Answer: a, Question: questions[a.QuestionId]))
            .Where(x => x.Question.InteractionType != OpenTextGrader.Type)
            .Select(x => new StudentAnswer(x.Question, x.Answer.At, x.Answer.TimeTaken, x.Answer.Grade(x.Question)))
            .ToList();
    }

    private async Task<List<RawAnswer>> ReadTestAnswersAsync(int studentId, DateTime from, DateTime to, CancellationToken ct)
    {
        // Süre dolup kapanmamış oturumun EndTime'ı boştur: başlangıcı süre sınırı kadar geriden aranır.
        var earliestStart = from.AddSeconds(-WorksheetDurationLimits.MaxSeconds);
        var instances = await db.TestInstances.AsNoTracking()
            .Where(i => i.StudentId == studentId)
            // Gezinme değil: silinen testin cevapları öğrencinin geçmişinden düşmesin.
            .Where(i => !db.Worksheets.IgnoreQueryFilters().Any(w => w.Id == i.WorksheetId && w.Kind == WorksheetKind.Activity))
            .Where(i => i.EndTime >= from || (i.EndTime == null && i.StartTime >= earliestStart))
            .Select(i => new { i.Id, i.Status, i.StartTime, i.EndTime, i.MaxDurationSeconds })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var finishedAt = instances
            // Sınav bütünlüğü: süren oturumun cevabı öğrenciye de asistana da doğrusunu söylemez.
            .Where(i => TestTimeLimit.EffectiveStatus(i.Status, i.StartTime, i.MaxDurationSeconds, now) != WorksheetInstanceStatus.Started)
            .Select(i => (i.Id, At: i.EndTime ?? TestTimeLimit.EndsAt(i.StartTime, i.MaxDurationSeconds) ?? i.StartTime))
            .Where(x => x.At >= from && x.At < to)
            .ToDictionary(x => x.Id, x => x.At);
        var ids = finishedAt.Keys.ToList();

        // Oturum satırından okuma kuralı (#684): sonradan kaldırılan madde de sayılır.
        var rows = await db.TestInstanceQuestions.AsNoTracking().WithRemovedWorksheetItems()
            .Where(r => ids.Contains(r.WorksheetInstanceId))
            .Select(r => new { r.WorksheetInstanceId, r.WorksheetQuestion.QuestionId, r.SelectedAnswerId, HasPayload = r.AnswerPayload != null, r.IsCorrect, r.TimeTaken })
            .ToListAsync(ct);

        return rows.Select(r => new RawAnswer(r.QuestionId, finishedAt[r.WorksheetInstanceId], r.TimeTaken, question =>
            WorksheetScoring.IsBlank(r.SelectedAnswerId, r.HasPayload) ? StudentAnswerOutcome.Blank
            : WorksheetScoring.IsCorrect(r.SelectedAnswerId, question.CorrectAnswerId, r.HasPayload, r.IsCorrect, questionKnown: !question.Deleted) ? StudentAnswerOutcome.Correct
            : StudentAnswerOutcome.Wrong)).ToList();
    }

    private async Task<List<RawAnswer>> ReadPracticeAnswersAsync(int studentId, DateTime from, DateTime to, CancellationToken ct)
    {
        // Gösterilip cevaplanmamış pratik sorusu sayılmaz; yalnız atlanan boştur.
        var rows = await db.PracticeSessionQuestions.AsNoTracking()
            .Where(r => r.PracticeSession.StudentId == studentId && (r.PracticeSession.EndTime == null || r.PracticeSession.EndTime >= from))
            .Where(r => r.AnsweredAt != null || r.IsSkipped)
            .Where(r => (r.AnsweredAt ?? r.ShownAt) >= from && (r.AnsweredAt ?? r.ShownAt) < to)
            .Select(r => new { r.QuestionId, At = r.AnsweredAt ?? r.ShownAt, r.IsSkipped, r.IsCorrect, r.TimeTaken })
            .ToListAsync(ct);

        return rows.Select(r => new RawAnswer(r.QuestionId, r.At, r.TimeTaken, _ =>
            r.IsSkipped ? StudentAnswerOutcome.Blank : r.IsCorrect ? StudentAnswerOutcome.Correct : StudentAnswerOutcome.Wrong)).ToList();
    }

    private async Task<Dictionary<int, AnsweredQuestion>> ReadQuestionsAsync(List<int> ids, CancellationToken ct)
    {
        // Silinen soru da okunur: boş değilse yanlış sayılır (WorksheetScoring), konusu yine bellidir.
        var rows = await db.Questions.AsNoTracking().IgnoreQueryFilters()
            .Where(q => ids.Contains(q.Id))
            .Select(q => new
            {
                q.Id,
                q.IsDeleted,
                q.SubjectId,
                q.TopicId,
                q.CorrectAnswerId,
                q.DifficultyLevel,
                q.InteractionType,
                SubTopics = q.QuestionSubTopics.Where(s => !s.IsDeleted).Select(s => new AnsweredSubTopic(s.SubTopicId, s.SubTopic.Name, s.SubTopic.TopicId)).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        return rows.ToDictionary(q => q.Id, q => new AnsweredQuestion(q.Id, q.IsDeleted, q.SubjectId, q.TopicId, q.CorrectAnswerId, q.DifficultyLevel, q.InteractionType, q.SubTopics.DistinctBy(s => s.Id).ToList()));
    }
}

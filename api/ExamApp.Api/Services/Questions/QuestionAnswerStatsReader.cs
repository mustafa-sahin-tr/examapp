using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Questions;

/// <summary>
/// Issue #1094: soru başına cevap istatistiği, exam DB'deki oturum satırlarından (event / ayrı tablo yok). Yalnız bitmiş
/// oturumun son cevabı sayılır; boş cevap cevaplanmamıştır (#785). Bir öğrencinin aynı soruyu birden fazla oturumda çözmesinde
/// en erken biten oturum sayılır: yeniden çözme (#609) oranı şişirmez, sayı farklı öğrencidir. #609 aynı oturumun satırlarını
/// yerinde yeniden yazdığı için o oturumda sayılan cevap son çözümdür. Açık uçlu (elle değerlendirilen) soru sayılmaz;
/// pratik oturumları dahil değildir.
/// </summary>
public static class QuestionAnswerStatsReader
{
    /// <param name="worksheetIds">Doluysa yalnız bu testlerin oturumları (ör. bir denemenin oturumları).</param>
    public static async Task<Dictionary<int, QuestionAnswerStatsDto>> ReadAsync(AppDbContext db, IReadOnlyCollection<int> questionIds,
        DateTime now, IReadOnlyCollection<int>? worksheetIds = null, CancellationToken ct = default)
    {
        if (questionIds.Count == 0)
            return [];

        var questions = await db.Questions.AsNoTracking().IgnoreQueryFilters()
            .Where(q => questionIds.Contains(q.Id) && q.InteractionType != OpenTextGrader.Type)
            .Select(q => new { q.Id, q.CorrectAnswerId, q.IsDeleted })
            .ToDictionaryAsync(q => q.Id, ct);
        var ids = questions.Keys.ToList();

        // Oturum satırından okuma kuralı (#684): sonradan kaldırılan madde de sayılır.
        var rows = db.TestInstanceQuestions.AsNoTracking().WithRemovedWorksheetItems()
            .Where(r => ids.Contains(r.WorksheetQuestion.QuestionId))
            .Where(r => r.SelectedAnswerId != null || r.AnswerPayload != null);
        if (worksheetIds is not null)
            rows = rows.Where(r => worksheetIds.Contains(r.WorksheetInstance.WorksheetId));
        var answers = await rows
            .Select(r => new
            {
                r.WorksheetQuestion.QuestionId,
                r.WorksheetInstance.StudentId,
                r.WorksheetInstanceId,
                r.WorksheetInstance.Status,
                r.WorksheetInstance.StartTime,
                r.WorksheetInstance.EndTime,
                r.WorksheetInstance.MaxDurationSeconds,
                r.SelectedAnswerId,
                HasPayload = r.AnswerPayload != null,
                r.IsCorrect,
                r.TimeTaken,
            })
            .ToListAsync(ct);

        return answers
            // Süren oturumun cevabı sayılmaz: öğrenci değiştirebilir.
            .Where(a => TestTimeLimit.EffectiveStatus(a.Status, a.StartTime, a.MaxDurationSeconds, now) != WorksheetInstanceStatus.Started)
            .GroupBy(a => (a.QuestionId, a.StudentId))
            .Select(g => g
                .OrderBy(a => a.EndTime ?? TestTimeLimit.EndsAt(a.StartTime, a.MaxDurationSeconds) ?? a.StartTime)
                .ThenBy(a => a.WorksheetInstanceId)
                .First())
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g =>
            {
                var question = questions[g.Key];
                var correct = g.Count(a => WorksheetScoring.IsCorrect(a.SelectedAnswerId, question.CorrectAnswerId, a.HasPayload, a.IsCorrect,
                    questionKnown: !question.IsDeleted));
                return QuestionAnswerStatsDto.From(g.Count(), correct, g.Sum(a => (long)a.TimeTaken));
            });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// Yanlış defteri (issue #698): testler ve pratikteki yanlışlar tek listede. Son yanlıştan sonra art arda
/// <see cref="CorrectToClear"/> doğru çözülen soru defterden çıkar; yeni yanlış onu geri getirir.
/// Tekrar listesiyle (#529) aynı dışlama: bitmemiş test/atamadaki ve bugünün günlük setinde bekleyen soru dönmez.
/// </summary>
internal static class MistakeNotebookQueries
{
    internal const int CorrectToClear = 2;

    /// <returns>null: soru defterde değil. <c>CorrectSince</c>: son yanlıştan sonraki doğru sayısı (#862 ilerleme);
    /// <c>LastWrongAnswerId</c>: son yanlışta seçilen şık (#862 "senin cevabın").</returns>
    internal static (DateTime LastWrongAt, int WrongCount, int CorrectSince, int? LastWrongAnswerId)? Evaluate(IEnumerable<DueReviewAttempt> attempts)
    {
        var ordered = attempts.OrderBy(a => a.AtUtc).ToList();
        var lastWrong = ordered.FindLastIndex(a => !a.Correct);
        var correctSince = ordered.Count - lastWrong - 1;
        if (lastWrong < 0 || correctSince >= CorrectToClear)
            return null;

        return (ordered[lastWrong].AtUtc, ordered.Count(a => !a.Correct), correctSince, ordered[lastWrong].SelectedAnswerId);
    }

    internal static async Task<List<MistakeItemDto>> LoadAsync(
        AppDbContext context,
        ILocalDayCalendar calendar,
        StudentProfileDto student,
        IReadOnlyCollection<int> subjectIds,
        IReadOnlyCollection<int> topicIds,
        CancellationToken ct)
    {
        var attempts = await DueReviewQueries.LoadAttemptsAsync(context, student.Id, ct);
        var active = attempts
            .Select(a => (QuestionId: a.Key, Status: Evaluate(a.Value)))
            .Where(x => x.Status != null)
            .ToDictionary(x => x.QuestionId, x => x.Status!.Value);

        if (active.Count == 0)
            return new List<MistakeItemDto>();

        var blocked = await DueReviewQueries.LoadBlockedAsync(context, calendar, student, ct);
        var candidateIds = active.Keys.Where(id => !blocked.Contains(id)).ToList();

        var questions = await PracticeQuestionPool.Build(context, student.GradeId!.Value, subjectIds, topicIds)
            .Where(q => candidateIds.Contains(q.Id))
            .Select(q => new
            {
                q.Id,
                q.SubjectId,
                SubjectName = q.Subject != null ? q.Subject.Name : null,
                q.TopicId,
                TopicName = q.Topic != null ? q.Topic.Name : null,
                // #862: satırda soru ve "senin cevabın / doğru cevap" (şık harfi sunucu sırasından, çözme ekranıyla aynı).
                q.ImageUrl,
                q.Text,
                q.CorrectAnswerId,
                AnswerIds = q.Answers.OrderBy(a => a.Order ?? int.MaxValue).ThenBy(a => a.Id).Select(a => a.Id).ToList()
            })
            .ToListAsync(ct);

        var reasons = await context.StudentMistakeReasons
            .AsNoTracking()
            .Where(r => r.StudentId == student.Id && candidateIds.Contains(r.QuestionId))
            .ToDictionaryAsync(r => r.QuestionId, r => r.Reason, ct);

        return questions
            .Select(q => new MistakeItemDto
            {
                QuestionId = q.Id,
                SubjectId = q.SubjectId,
                SubjectName = q.SubjectName,
                TopicId = q.TopicId,
                TopicName = q.TopicName,
                LastWrongAt = active[q.Id].LastWrongAt,
                WrongCount = active[q.Id].WrongCount,
                Reason = reasons.TryGetValue(q.Id, out var reason) ? reason.ToString() : null,
                ImageUrl = string.IsNullOrWhiteSpace(q.ImageUrl) ? null : q.ImageUrl,
                Text = string.IsNullOrWhiteSpace(q.Text) ? null : q.Text,
                YourAnswer = AnswerLetter(q.AnswerIds, active[q.Id].LastWrongAnswerId),
                CorrectAnswer = AnswerLetter(q.AnswerIds, q.CorrectAnswerId),
                CorrectSinceWrong = active[q.Id].CorrectSince,
                CorrectToClear = CorrectToClear
            })
            .OrderByDescending(i => i.LastWrongAt)
            .ThenBy(i => i.QuestionId)
            .ToList();
    }

    /// <summary>Şıkkın harfi (A, B, …); şık yoksa / bulunamazsa null.</summary>
    internal static string? AnswerLetter(List<int> answerIds, int? answerId)
    {
        if (answerId is not { } id)
            return null;
        var index = answerIds.IndexOf(id);
        return index is >= 0 and < 26 ? ((char)('A' + index)).ToString() : null;
    }
}

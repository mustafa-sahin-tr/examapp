using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// "Günün soruları" (issue #99, CR U1): setin soru listesinin TEK kaynağı. Yalnız canlı item'lar (soru gün içinde soft-delete
/// edilmemiş), <see cref="DailyQuestionSetItem.Order"/> sırasıyla. Total, Completed hesabı, <c>next</c> sırası ve otomatik
/// kapanma hep bu listeye göre yapılır — silinen soru seti "bitirilemez" hale getirmez.
/// </summary>
internal static class DailySetQueries
{
    public static Task<List<int>> LoadLiveQuestionIdsAsync(AppDbContext context, int setId, CancellationToken ct) =>
        context.DailyQuestionSetItems
            .AsNoTracking()
            .Where(i => i.DailyQuestionSetId == setId && !i.Question.IsDeleted)
            .OrderBy(i => i.Order)
            .Select(i => i.QuestionId)
            .ToListAsync(ct);

    /// <summary>Bugünün setinde olup günlük oturumda henüz cevaplanmamış soru id'leri (serbest pratik ve tekrar dışlaması, security O1).</summary>
    internal static Task<List<int>> LoadTodaysUnansweredQuestionIdsAsync(
        AppDbContext context, ILocalDayCalendar calendar, int studentId, CancellationToken ct)
    {
        var today = calendar.Today;
        return context.DailyQuestionSetItems
            .AsNoTracking()
            .Where(i => i.DailyQuestionSet.StudentId == studentId && i.DailyQuestionSet.Day == today)
            .Where(i => !context.PracticeSessionQuestions.Any(p =>
                p.PracticeSessionId == i.DailyQuestionSet.PracticeSessionId
                && p.QuestionId == i.QuestionId
                && p.AnsweredAt != null))
            .Select(i => i.QuestionId)
            .ToListAsync(ct);
    }
}

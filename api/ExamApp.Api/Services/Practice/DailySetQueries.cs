using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
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
}

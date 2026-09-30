using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Kullanıcıya gösterilen "Soru n" numarasının TEK tanımı (issue #309).
///
/// UI numarayı soru listesinin konumundan üretir (test çözme ekranı ve detaydaki soru gezgini: <c>index + 1</c>); liste
/// sınav başlatılırken <c>TestQuestions</c>'ın <c>Order</c> sırasıyla, silinmiş satırlar ve sorusu silinmiş satırlar hariç
/// kurulur. <see cref="WorksheetQuestion.Order"/> ise kaynağa göre 0 ya da 1 tabanlı yazılır ve silmeden sonra boşluklu
/// kalır; ham değeri numara olarak göstermek yanlıştır.
///
/// Kural: worksheet'in silinmemiş, sorusu silinmemiş <see cref="WorksheetQuestion"/> satırları <c>(Order, Id)</c>'ye göre
/// sıralanır; numara = konum + 1. <c>Id</c> eşit <c>Order</c>'larda kararlı sıra verir.
/// </summary>
public static class WorksheetQuestionNumbering
{
    /// <summary>
    /// Bellekteki satırlar için <c>WorksheetQuestion.Id → numara</c>. Çağıran yalnızca numaralanan kümeyi verir
    /// (global filtreyle yüklenmiş, <c>Question</c>'ı null olmayan satırlar — bkz. sınıf özeti).
    /// </summary>
    public static IReadOnlyDictionary<int, int> NumberByWorksheetQuestionId(IEnumerable<WorksheetQuestion> numbered) =>
        numbered
            .OrderBy(wq => wq.Order)
            .ThenBy(wq => wq.Id)
            .Select((wq, index) => (wq.Id, Number: index + 1))
            .ToDictionary(x => x.Id, x => x.Number);

    /// <summary>
    /// Toplu varyant (issue #305, code review O2): birden çok <c>(worksheetId, questionId)</c> çifti için TEK sorgu —
    /// ilgili worksheet'lerin numaralanan satırları (<see cref="ResolveNumberAsync"/> ile aynı küme: silinmemiş satır +
    /// sorusu silinmemiş) çekilir, numara bellekte <c>(Order, Id)</c> konumundan üretilir. Aynı soru iki kez varsa ilk
    /// konumu. Worksheet'te olmayan çift sözlükte yer almaz.
    /// </summary>
    public static async Task<IReadOnlyDictionary<(int WorksheetId, int QuestionId), int>> ResolveNumbersAsync(
        AppDbContext db, IEnumerable<(int WorksheetId, int QuestionId)> pairs, CancellationToken ct = default)
    {
        var wanted = pairs.ToHashSet();
        var result = new Dictionary<(int, int), int>();
        if (wanted.Count == 0)
            return result;

        var worksheetIds = wanted.Select(p => p.WorksheetId).Distinct().ToList();
        var rows = await db.TestQuestions
            .AsNoTracking()
            .Where(tq => worksheetIds.Contains(tq.TestId) && db.Questions.Any(q => q.Id == tq.QuestionId))
            .Select(tq => new { tq.Id, tq.TestId, tq.QuestionId, tq.Order })
            .ToListAsync(ct);

        foreach (var group in rows.GroupBy(r => r.TestId))
        {
            var number = 0;
            foreach (var row in group.OrderBy(r => r.Order).ThenBy(r => r.Id))
            {
                number++;
                var key = (group.Key, row.QuestionId);
                if (wanted.Contains(key))
                    result.TryAdd(key, number);
            }
        }

        return result;
    }

    /// <summary>
    /// Sorunun worksheet içindeki numarası; soru bu worksheet'te değilse null. Tek sorgu: "benden önce gelen numaralanan
    /// kardeş sayısı + 1" (korelasyonlu alt sorgu; SQLite ve PostgreSQL'de aynı). Aynı soru iki kez varsa ilk konumu.
    /// </summary>
    public static Task<int?> ResolveNumberAsync(AppDbContext db, int worksheetId, int questionId, CancellationToken ct = default) =>
        db.TestQuestions
            .AsNoTracking()
            .Where(tq => tq.TestId == worksheetId && tq.QuestionId == questionId)
            .OrderBy(tq => tq.Order)
            .ThenBy(tq => tq.Id)
            .Select(tq => (int?)(db.TestQuestions.Count(o => o.TestId == worksheetId
                && db.Questions.Any(q => q.Id == o.QuestionId)
                && (o.Order < tq.Order || (o.Order == tq.Order && o.Id < tq.Id))) + 1))
            .FirstOrDefaultAsync(ct);
}

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Issue #580: onaylanmamış çözüm yayımlanamaz. Sayfada (tür fark etmez — review C2: onaylanmamış soru "Havuzdan ekle" ile bir teste
/// de bağlanabilir) <see cref="SolutionStatus.Pending"/> çözümlü (cevap anahtarından ya da AI, henüz Admin/QuestionTrainer onaylamamış)
/// en az bir soru varsa sayfa öğrenciye atanamaz (<c>AssignWorksheetAsync</c>), öğrenci yeni oturum başlatamaz (<c>StartTestAsync</c>)
/// ve keşfet listesinde görünmez (öğrencinin daha önce açtığı oturum listede kalır). Pratik havuzu ve "Yanlışlarım" sayfası bu soruları
/// almaz. Silinmiş sorular / üyelikler (soft-delete filtresi) sayılmaz.
/// </summary>
public static class WorksheetSolutionGate
{
    /// <summary>Sayfada onay bekleyen çözümlü (silinmemiş) soru var mı.</summary>
    public static Task<bool> HasPendingSolutionAsync(AppDbContext context, int worksheetId, CancellationToken ct = default) =>
        PendingWorksheetIds(context).AnyAsync(id => id == worksheetId, ct);

    /// <summary>Onay bekleyen çözümlü sorusu olan sayfa id'leri (sorgu; SQL'e çevrilir).</summary>
    public static IQueryable<int> PendingWorksheetIds(AppDbContext context) =>
        context.TestQuestions
            .Where(tq => tq.Question.SolutionStatus == SolutionStatus.Pending)
            .Select(tq => tq.TestId);
}

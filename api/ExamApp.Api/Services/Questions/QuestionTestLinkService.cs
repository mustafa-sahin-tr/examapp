using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Questions;

/// <summary>
/// issue #472: test ekranındaki "Havuzdan ekle" — hazır soruları teste BAĞLAR (kopyalamaz; test kopyalamayla aynı model,
/// tek soru satırı birden çok testte). Soruyu düzenleme yetkisi değişmez: yalnız sahibi/admin (<see cref="IQuestionOwnershipGuard.CanModifyQuestionAsync"/>).
/// Test yazma yetkisi (sahibi/admin) çağıranın (controller) sorumluluğundadır.
/// </summary>
public interface IQuestionTestLinkService
{
    /// <summary>
    /// Soruları testin sonuna istek sırasıyla ekler; testte zaten olanlar atlanır. Test yoksa ya da bağlanamayan (yok ya
    /// da çağıranın kullanamadığı) tek bir soru bile varsa hiçbir şey yazılmaz ve <c>null</c> döner (varlık sızdırılmaz → 404).
    /// </summary>
    Task<LinkBankQuestionsResultDto?> LinkAsync(int worksheetId, IReadOnlyList<int> questionIds, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// issue #685: testin maddelerini <paramref name="questionIds"/> sırasıyla yeniden numaralar (<c>Order</c> = 1..n), tek
    /// transaction'da ve bağlamayla aynı test kilidinin altında. Liste testin sorusu silinmemiş bütün maddeleriyle birebir
    /// eşleşmeli (<see cref="ReorderTestQuestionsResult.Mismatch"/>); sorusu silinmiş (listede görünmeyen) maddeler sona eklenir.
    /// Başlamış oturumlar etkilenmez: oturum sırası başlarken kopyalanır (<c>WorksheetInstanceQuestion.Order</c>).
    /// Test yazma yetkisi çağıranın (controller) sorumluluğundadır.
    /// </summary>
    Task<ReorderTestQuestionsResult> ReorderAsync(int worksheetId, IReadOnlyList<int> questionIds, int userId, CancellationToken ct = default);
}

public sealed class QuestionTestLinkService(AppDbContext context) : IQuestionTestLinkService
{
    public async Task<LinkBankQuestionsResultDto?> LinkAsync(int worksheetId, IReadOnlyList<int> questionIds, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var ids = questionIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0 || (!isAdmin && userId <= 0))
            return null;

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Retry'da önceki denemenin Added satırları ikinci kez yazılmasın.
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            await context.Database.AcquireWorksheetQuestionLinkLockAsync(worksheetId, ct);

            // Kullanılabilirlik kilit altında: kontrol ile yazma arasında soru/test silinemez ya da gizlenemez (TOCTOU).
            var worksheet = await context.Worksheets.AsNoTracking()
                .Where(w => w.Id == worksheetId)
                .Select(w => new { w.CreateUserId, w.SourceWorksheetId, w.Kind })
                .FirstOrDefaultAsync(ct);
            if (worksheet == null)
                return null;

            // Legacy sahiplik "soruyu içeren kopya olmayan testin sahibi" kuralıyla türetilir: sahipli orijinal teste bağlanan
            // sahipsiz soru o testin sahibine düzenleme yetkisi verirdi. Bu durumda yalnız test sahibinin zaten sahip olduğu
            // legacy sorular kabul edilir (admin başkasının testine eklerken de).
            var ownerId = worksheet.CreateUserId ?? 0;
            var grantsLegacyOwnership = worksheet.SourceWorksheetId == null && ownerId > 0;
            if (!await AllLinkableAsync(ids, userId, isAdmin, ownerId, grantsLegacyOwnership, ct))
                return null;

            // issue #583: açık uçlu madde teste bağlanamaz (yalnız etkinlik) — hiçbir şey yazılmaz, 400.
            if (worksheet.Kind == WorksheetKind.Test && await Worksheets.OpenTextTestGuard.AnyOpenTextAsync(context, ids, ct))
            {
                await transaction.RollbackAsync(ct);
                return new LinkBankQuestionsResultDto { ErrorCode = Worksheets.OpenTextTestGuard.ErrorCode };
            }

            // Testin mevcut (silinmemiş) üyelikleri ve son sıra numarası.
            var existing = await context.TestQuestions.AsNoTracking()
                .Where(tq => tq.TestId == worksheetId)
                .Select(tq => new { tq.QuestionId, tq.Order })
                .ToListAsync(ct);
            var inTest = existing.Select(e => e.QuestionId).ToHashSet();
            var nextOrder = existing.Count == 0 ? 1 : existing.Max(e => e.Order) + 1;

            var result = new LinkBankQuestionsResultDto();
            foreach (var id in ids)
            {
                if (inTest.Contains(id))
                {
                    result.AlreadyInTestQuestionIds.Add(id);
                    continue;
                }

                context.TestQuestions.Add(new WorksheetQuestion { TestId = worksheetId, QuestionId = id, Order = nextOrder++ });
                result.AddedQuestionIds.Add(id);
            }

            if (result.AddedQuestionIds.Count > 0)
            {
                context.SetCurrentUser(userId);
                await context.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return result;
        });
    }

    public async Task<ReorderTestQuestionsResult> ReorderAsync(int worksheetId, IReadOnlyList<int> questionIds, int userId, CancellationToken ct = default)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            await context.Database.AcquireWorksheetQuestionLinkLockAsync(worksheetId, ct);

            if (!await context.Worksheets.AsNoTracking().AnyAsync(w => w.Id == worksheetId, ct))
                return ReorderTestQuestionsResult.NotFound;

            // Silinmemiş bütün maddeler; "görünür" = sorusu silinmemiş (madde listesi / bytest ile aynı küme).
            var items = await context.TestQuestions
                .Where(tq => tq.TestId == worksheetId)
                .OrderBy(tq => tq.Order).ThenBy(tq => tq.Id)
                .ToListAsync(ct);
            var visibleIds = (await context.TestQuestions.AsNoTracking()
                .Where(tq => tq.TestId == worksheetId && tq.Question.Id > 0)
                .Select(tq => tq.Id)
                .ToListAsync(ct)).ToHashSet();

            var visible = items.Where(i => visibleIds.Contains(i.Id)).ToList();
            var visibleQuestionIds = visible.Select(i => i.QuestionId).ToHashSet();
            if (visibleQuestionIds.Count != questionIds.Count || !questionIds.All(visibleQuestionIds.Contains))
                return ReorderTestQuestionsResult.Mismatch;

            // Aynı soru birden çok kez bağlıysa (legacy) kendi aralarındaki sırası korunur.
            var position = questionIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            var ordered = visible
                .OrderBy(i => position[i.QuestionId])
                .Concat(items.Where(i => !visibleIds.Contains(i.Id)))
                .ToList();

            var order = 1;
            foreach (var item in ordered)
                item.Order = order++;

            if (context.ChangeTracker.HasChanges())
            {
                context.SetCurrentUser(userId);
                await context.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return ReorderTestQuestionsResult.Reordered;
        });
    }

    /// <summary>
    /// Sahibi damgalı (<c>CreateUserId &gt; 0</c>) soru: admin için her biri; öğretmen için kendi sorusu ya da görebildiği bir
    /// teste bağlı soru (<see cref="QuestionOwnershipGuard.CanViewQuestionAsync"/> kuralı). Sahipsiz (legacy) soru: hedef test
    /// legacy sahiplik vermiyorsa (kopya ya da sahipsiz test) admin için serbest; aksi halde test sahibinin zaten sahip olduğu
    /// (onun kopya olmayan bir testinde bulunan) soru. Öğretmen yalnız kendi legacy sorusunu bağlar.
    /// </summary>
    private async Task<bool> AllLinkableAsync(int[] ids, int userId, bool isAdmin, int ownerId, bool grantsLegacyOwnership, CancellationToken ct)
    {
        var questions = context.Questions.AsNoTracking().Where(q => ids.Contains(q.Id));

        int linkable;
        if (isAdmin)
        {
            linkable = await questions
                .Where(q =>
                    q.CreateUserId > 0
                    || !grantsLegacyOwnership
                    || context.TestQuestions.Any(tq =>
                        tq.QuestionId == q.Id && tq.Worksheet.SourceWorksheetId == null && tq.Worksheet.CreateUserId == ownerId))
                .CountAsync(ct);
        }
        else
        {
            var schoolId = await context.ResolveTeacherSchoolIdAsync(userId, ct);
            var visibleWorksheets = context.Worksheets.Where(WorksheetAccess.VisibleToTeacherPredicate(context, userId, schoolId));
            linkable = await questions
                .Where(q =>
                    q.CreateUserId == userId
                    || (q.CreateUserId > 0 && context.TestQuestions.Any(tq => tq.QuestionId == q.Id && visibleWorksheets.Any(w => w.Id == tq.TestId)))
                    || ((q.CreateUserId == null || q.CreateUserId == 0) && context.TestQuestions.Any(tq =>
                        tq.QuestionId == q.Id && tq.Worksheet.SourceWorksheetId == null && tq.Worksheet.CreateUserId == userId)))
                .CountAsync(ct);
        }

        return linkable == ids.Length;
    }
}

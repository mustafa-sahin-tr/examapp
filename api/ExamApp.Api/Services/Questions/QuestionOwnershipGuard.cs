using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Foundation.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace ExamApp.Api.Services.Questions;

/// <summary>issue #287 (security review H1): soru bankası uçlarının policy adları.</summary>
public static class QuestionAccessPolicies
{
    /// <summary>Teacher, Admin ya da servis hesabı (BadgeService sınıflandırıcısı: image + classification).</summary>
    public const string TeacherAdminOrService = "TeacherAdminOrService";

    /// <summary>Admin ya da servis hesabı (classifier-cache işaretçisi — yalnızca servis-servis).</summary>
    public const string AdminOrService = "AdminOrService";

    /// <summary>Policy'leri kaydeder (Program.cs ve testler aynı tanımı kullanır).</summary>
    public static void AddTo(AuthorizationOptions options, string[]? serviceClients)
    {
        options.AddPolicy(TeacherAdminOrService, policy =>
            policy.RequireAssertion(context =>
                context.User.IsInRole("Teacher") || context.User.IsInRole("Admin") ||
                ServicePrincipal.IsService(context.User, serviceClients)));

        options.AddPolicy(AdminOrService, policy =>
            policy.RequireAssertion(context =>
                context.User.IsInRole("Admin") || ServicePrincipal.IsService(context.User, serviceClients)));
    }
}

/// <summary>Soru / test üzerinde yazma yetkisinin sonucu.</summary>
public enum QuestionAccessResult
{
    Allowed = 0,
    NotFound = 1,
    Forbidden = 2
}

/// <summary>
/// issue #287 (security review H1): soru bankası YAZMA uçlarında kaynak sahipliği. Admin her şeyi değiştirebilir;
/// öğretmen yalnızca kendi sorusunu / kendi testini. Servis hesabı muafiyeti çağıranın sorumluluğundadır
/// (yalnızca sınıflandırma ucunda verilir).
/// </summary>
public interface IQuestionOwnershipGuard
{
    /// <summary>
    /// Soru sahibi: <c>Question.CreateUserId</c> (#287 sonrası oluşturulanlarda damgalanır). Eski (CreateUserId 0/null)
    /// sorularda sahiplik, soruyu içeren KOPYA OLMAYAN (<c>SourceWorksheetId == null</c>) bir testin sahibinden türetilir —
    /// test kopyalama soru satırlarını paylaştığı için kopyalayan öğretmen orijinal soruyu değiştiremez.
    /// </summary>
    Task<QuestionAccessResult> CanModifyQuestionAsync(int questionId, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Test (worksheet) sahibi ya da admin — <see cref="WorksheetAccess.CanModify"/> ile aynı kural.</summary>
    Task<QuestionAccessResult> CanModifyWorksheetAsync(int worksheetId, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// issue #402 (P2): testin sorularını OKUMA — <see cref="WorksheetAccess.CanView"/> ile aynı kural (sahibi/admin,
    /// PublicView/PublicAssignable, aynı okul için SchoolOnly; okul değerleri DB'den). Legacy (sahipsiz) test yalnız admin.
    /// </summary>
    Task<QuestionAccessResult> CanViewWorksheetAsync(int worksheetId, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// issue #402 (P3): tek soruyu OKUMA. Admin; soru sahibi (<see cref="CanModifyQuestionAsync"/> kuralı); ya da soruyu
    /// (silinmemiş üyelikle) içeren en az bir test öğretmene <see cref="WorksheetAccess.VisibleToTeacherPredicate"/>
    /// ile görünür (kendi testi — kopyası dahil —, public ya da aynı okul SchoolOnly).
    /// </summary>
    Task<QuestionAccessResult> CanViewQuestionAsync(int questionId, int userId, bool isAdmin, CancellationToken ct = default);

    /// <summary>
    /// issue #402 (P5): var olan bir paragrafı (Passage) id ile soruya bağlama. Admin; paragrafı oluşturan
    /// (<c>Passage.CreateUserId</c>); paragraf zaten <paramref name="questionId"/> sorusuna bağlıysa (düzenlemede aynı
    /// paragrafı geri göndermek); legacy (CreateUserId 0/null) paragrafta ona bağlı sorulardan birinin sahibi.
    /// </summary>
    Task<QuestionAccessResult> CanUsePassageAsync(int passageId, int userId, bool isAdmin, int? questionId = null, CancellationToken ct = default);
}

public sealed class QuestionOwnershipGuard : IQuestionOwnershipGuard
{
    private readonly AppDbContext _context;

    public QuestionOwnershipGuard(AppDbContext context) => _context = context;

    /// <summary>
    /// Soru sahipliği kuralının TEK kaynağı (EF'e çevrilebilir; <see cref="CanModifyQuestionAsync"/> ve issue #570 çözüm
    /// görünürlüğü <c>QuestionQueryService</c> aynı ağacı kullanır): damgalı soruda <c>CreateUserId == userId</c>; eski
    /// (CreateUserId 0/null) soruda soruyu içeren KOPYA OLMAYAN bir testin sahibi. <paramref name="userId"/> &lt;= 0 → false.
    /// Admin muafiyeti çağıranın işidir.
    /// </summary>
    public static Expression<Func<Question, bool>> OwnedBy(AppDbContext context, int userId) =>
        q => userId > 0 && ((q.CreateUserId ?? 0) > 0
            ? q.CreateUserId == userId
            : context.TestQuestions.Any(tq =>
                tq.QuestionId == q.Id
                && tq.Worksheet.SourceWorksheetId == null
                && tq.Worksheet.CreateUserId == userId));

    public async Task<QuestionAccessResult> CanModifyQuestionAsync(int questionId, int userId, bool isAdmin, CancellationToken ct = default)
    {
        // Tek sorgu: satır yoksa null (NotFound), varsa sahiplik sonucu.
        var owns = await _context.Questions.AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(OwnedBy(_context, userId))
            .Select(o => (bool?)o)
            .FirstOrDefaultAsync(ct);

        if (owns == null)
            return QuestionAccessResult.NotFound;
        if (isAdmin)
            return QuestionAccessResult.Allowed;
        if (userId <= 0)
            return QuestionAccessResult.Forbidden;

        return owns.Value ? QuestionAccessResult.Allowed : QuestionAccessResult.Forbidden;
    }

    public async Task<QuestionAccessResult> CanModifyWorksheetAsync(int worksheetId, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var worksheet = await _context.Worksheets.AsNoTracking()
            .Where(w => w.Id == worksheetId)
            .Select(w => new { w.CreateUserId })
            .FirstOrDefaultAsync(ct);

        if (worksheet == null)
            return QuestionAccessResult.NotFound;

        return WorksheetAccess.CanModify(worksheet.CreateUserId, userId, isAdmin)
            ? QuestionAccessResult.Allowed
            : QuestionAccessResult.Forbidden;
    }

    public async Task<QuestionAccessResult> CanViewWorksheetAsync(int worksheetId, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var worksheet = await _context.Worksheets.AsNoTracking()
            .Where(w => w.Id == worksheetId)
            .Select(w => new Worksheet
            {
                Id = w.Id,
                CreateUserId = w.CreateUserId,
                TeacherSharing = w.TeacherSharing,
                StudentVisibility = w.StudentVisibility
            })
            .FirstOrDefaultAsync(ct);

        if (worksheet == null)
            return QuestionAccessResult.NotFound;
        if (isAdmin)
            return QuestionAccessResult.Allowed;
        if (userId <= 0)
            return QuestionAccessResult.Forbidden;

        var (ownerSchoolId, requesterSchoolId) = await _context.ResolveSchoolContextAsync(worksheet, userId, isAdmin, ct);
        return WorksheetAccess.CanView(worksheet.CreateUserId, userId, isAdmin, worksheet.TeacherSharing,
                worksheet.StudentVisibility, requesterSchoolId, ownerSchoolId)
            ? QuestionAccessResult.Allowed
            : QuestionAccessResult.Forbidden;
    }

    public async Task<QuestionAccessResult> CanViewQuestionAsync(int questionId, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var owner = await CanModifyQuestionAsync(questionId, userId, isAdmin, ct);
        if (owner != QuestionAccessResult.Forbidden)
            return owner; // NotFound ya da Allowed (admin / sahibi)
        if (userId <= 0)
            return QuestionAccessResult.Forbidden;

        var requesterSchoolId = await _context.ResolveTeacherSchoolIdAsync(userId, ct);
        // İndeksli taraftan (WorksheetQuestions.QuestionId FK indeksi): sorunun (silinmemiş) üyelikleri → testleri → görünürlük.
        var visible = await _context.TestQuestions.AsNoTracking()
            .Where(tq => tq.QuestionId == questionId)
            .Select(tq => tq.Worksheet)
            .Where(WorksheetAccess.VisibleToTeacherPredicate(_context, userId, requesterSchoolId))
            .AnyAsync(ct);

        return visible ? QuestionAccessResult.Allowed : QuestionAccessResult.Forbidden;
    }

    public async Task<QuestionAccessResult> CanUsePassageAsync(int passageId, int userId, bool isAdmin, int? questionId = null, CancellationToken ct = default)
    {
        var row = await _context.Passage.AsNoTracking()
            .Where(p => p.Id == passageId)
            .Select(p => new
            {
                p.CreateUserId,
                LinkedToQuestion = questionId != null && p.Questions.Any(q => q.Id == questionId),
                // Legacy paragraf: bağlı sorulardan biri kullanıcının (damgalı sahip ya da eski soruda orijinal testin sahibi).
                OwnsLinkedQuestion = userId > 0 && p.Questions.Any(q =>
                    q.CreateUserId == userId
                    || ((q.CreateUserId == null || q.CreateUserId == 0) && _context.TestQuestions.Any(tq =>
                        tq.QuestionId == q.Id
                        && tq.Worksheet.SourceWorksheetId == null
                        && tq.Worksheet.CreateUserId == userId)))
            })
            .FirstOrDefaultAsync(ct);

        if (row == null)
            return QuestionAccessResult.NotFound;
        if (isAdmin)
            return QuestionAccessResult.Allowed;
        if (userId <= 0)
            return QuestionAccessResult.Forbidden;
        if (row.LinkedToQuestion)
            return QuestionAccessResult.Allowed;

        var createdBy = row.CreateUserId ?? 0;
        var owns = createdBy > 0 ? createdBy == userId : row.OwnsLinkedQuestion;
        return owns ? QuestionAccessResult.Allowed : QuestionAccessResult.Forbidden;
    }
}

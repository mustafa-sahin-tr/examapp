using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Foundation.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

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
}

public sealed class QuestionOwnershipGuard : IQuestionOwnershipGuard
{
    private readonly AppDbContext _context;

    public QuestionOwnershipGuard(AppDbContext context) => _context = context;

    public async Task<QuestionAccessResult> CanModifyQuestionAsync(int questionId, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var row = await _context.Questions.AsNoTracking()
            .Where(q => q.Id == questionId)
            .Select(q => new
            {
                q.CreateUserId,
                OwnsOriginalWorksheet = userId > 0 && _context.TestQuestions.Any(tq =>
                    tq.QuestionId == q.Id
                    && tq.Worksheet.SourceWorksheetId == null
                    && tq.Worksheet.CreateUserId == userId)
            })
            .FirstOrDefaultAsync(ct);

        if (row == null)
            return QuestionAccessResult.NotFound;
        if (isAdmin)
            return QuestionAccessResult.Allowed;
        if (userId <= 0)
            return QuestionAccessResult.Forbidden;

        var createdBy = row.CreateUserId ?? 0;
        var owns = createdBy > 0 ? createdBy == userId : row.OwnsOriginalWorksheet;
        return owns ? QuestionAccessResult.Allowed : QuestionAccessResult.Forbidden;
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
}

using ExamApp.Api.Data;
using ExamApp.Api.Services.Interactions;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// issue #583: açık uçlu (<c>openText</c>) madde yalnız etkinlikte (<c>Worksheet.Kind = Activity</c>) yer alır — test puanlıdır,
/// açık uçlu cevap otomatik puanlanamaz. Teste giriş yolları (soru kaydı / toplu kayıt, havuzdan ekle, test kopyalama, Activity → Test
/// tür değişimi, "yanlışlarım" testi) bu kurallarla reddeder (<c>OpenTextNotAllowedInTest</c>).
/// </summary>
public static class OpenTextTestGuard
{
    public const string ErrorCode = "OpenTextNotAllowedInTest";
    public const string MessageKey = "worksheets.openTextReview.notAllowedInTest";

    /// <summary>Sayfa var ve türü Test mi (silinmemiş).</summary>
    public static Task<bool> IsTestAsync(AppDbContext db, int worksheetId, CancellationToken ct = default)
        => db.Worksheets.AsNoTracking().AnyAsync(w => w.Id == worksheetId && w.Kind == WorksheetKind.Test, ct);

    /// <summary>Sorulardan biri açık uçlu mu.</summary>
    public static Task<bool> AnyOpenTextAsync(AppDbContext db, IReadOnlyCollection<int> questionIds, CancellationToken ct = default)
        => db.Questions.AsNoTracking().AnyAsync(q => questionIds.Contains(q.Id) && q.InteractionType == OpenTextGrader.Type, ct);

    /// <summary>Sayfada açık uçlu madde var mı.</summary>
    public static Task<bool> WorksheetHasOpenTextAsync(AppDbContext db, int worksheetId, CancellationToken ct = default)
        => db.TestQuestions.AsNoTracking()
            .AnyAsync(wq => wq.TestId == worksheetId && wq.Question.InteractionType == OpenTextGrader.Type, ct);

    /// <summary>Soru bir Test sayfasına bağlı mı (güncellemede tip openText'e çevrilirken).</summary>
    public static Task<bool> QuestionInAnyTestAsync(AppDbContext db, int questionId, CancellationToken ct = default)
        => db.TestQuestions.AsNoTracking()
            .AnyAsync(wq => wq.QuestionId == questionId && wq.Worksheet.Kind == WorksheetKind.Test, ct);
}

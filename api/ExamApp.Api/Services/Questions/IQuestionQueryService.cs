using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Questions;

/// <summary>Question/passage reads. Split out of the former god-class <c>QuestionService</c>.</summary>
public interface IQuestionQueryService
{
    /// <summary>
    /// Tek soru (düzenleme yanıtı). issue #570: plan çözümsüz döner; <c>InteractionSolution</c> yalnız
    /// <paramref name="solutionViewerIsAdmin"/> ya da <paramref name="solutionViewerUserId"/> sorunun sahibiyse
    /// (<see cref="IQuestionOwnershipGuard.CanModifyQuestionAsync"/> kuralı) dolar. Varsayılan: çözüm yok.
    /// </summary>
    Task<QuestionDto?> GetQuestionById(int id, int solutionViewerUserId = 0, bool solutionViewerIsAdmin = false,
        CancellationToken ct = default);
    /// <summary>
    /// issue #402 (P4): son 10 paragraf — admin tüm paragraflar, diğerleri yalnız kendi oluşturdukları
    /// (<c>Passage.CreateUserId == userId</c>). <paramref name="userId"/> &lt;= 0 (profil çözülemedi) → boş liste.
    /// </summary>
    Task<List<PassageDto>> GetLastTenPassages(int userId, bool isAdmin, CancellationToken ct = default);
    /// <summary>Testin soruları. issue #570: çözüm kuralı <see cref="GetQuestionById"/> ile aynı, soru bazında.</summary>
    Task<List<QuestionDto>> GetQuestionByTestId(int testid, int solutionViewerUserId = 0, bool solutionViewerIsAdmin = false,
        CancellationToken ct = default);

    /// <summary>
    /// issue #471: soru bankası araması (sayfalı, en yeni önce). Kapsam <c>mine</c>: çağıranın soruları
    /// (<see cref="IQuestionOwnershipGuard.CanModifyQuestionAsync"/> sahiplik kuralı). Kapsam <c>all</c>: admin için tüm
    /// sorular, öğretmen için <see cref="IQuestionOwnershipGuard.CanViewQuestionAsync"/> ile görebildiği sorular (kendi
    /// soruları + görebildiği bir teste bağlı olanlar). <paramref name="filter"/> önceden <see cref="QuestionBankQuery.IsValid"/>
    /// ile doğrulanmış olmalı. Admin olmayan <paramref name="userId"/> &lt;= 0 → boş sayfa.
    /// </summary>
    Task<QuestionBankPageDto> SearchBankAsync(QuestionBankQuery filter, int userId, bool isAdmin, CancellationToken ct = default);
}

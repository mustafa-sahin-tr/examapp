using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Questions;

/// <summary>Question/passage reads. Split out of the former god-class <c>QuestionService</c>.</summary>
public interface IQuestionQueryService
{
    Task<QuestionDto?> GetQuestionById(int id);
    /// <summary>
    /// issue #402 (P4): son 10 paragraf — admin tüm paragraflar, diğerleri yalnız kendi oluşturdukları
    /// (<c>Passage.CreateUserId == userId</c>). <paramref name="userId"/> &lt;= 0 (profil çözülemedi) → boş liste.
    /// </summary>
    Task<List<PassageDto>> GetLastTenPassages(int userId, bool isAdmin, CancellationToken ct = default);
    Task<List<QuestionDto>> GetQuestionByTestId(int testid);

    /// <summary>
    /// issue #471: soru bankası araması (sayfalı, en yeni önce). Kapsam <c>mine</c>: çağıranın soruları
    /// (<see cref="IQuestionOwnershipGuard.CanModifyQuestionAsync"/> sahiplik kuralı). Kapsam <c>all</c>: admin için tüm
    /// sorular, öğretmen için <see cref="IQuestionOwnershipGuard.CanViewQuestionAsync"/> ile görebildiği sorular (kendi
    /// soruları + görebildiği bir teste bağlı olanlar). <paramref name="filter"/> önceden <see cref="QuestionBankQuery.IsValid"/>
    /// ile doğrulanmış olmalı. Admin olmayan <paramref name="userId"/> &lt;= 0 → boş sayfa.
    /// </summary>
    Task<QuestionBankPageDto> SearchBankAsync(QuestionBankQuery filter, int userId, bool isAdmin, CancellationToken ct = default);
}

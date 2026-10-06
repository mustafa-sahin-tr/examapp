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
}

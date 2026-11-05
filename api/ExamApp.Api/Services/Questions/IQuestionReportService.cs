using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.QuestionReports;

namespace ExamApp.Api.Services.Questions;

public interface IQuestionReportService
{
    /// <summary>Soru öğrencinin o oturumunda değilse null (var olmayanla aynı yanıt, issue #402 deseni).</summary>
    Task<QuestionReportCreatedDto?> CreateAsync(int questionId, CreateQuestionReportDto dto, int studentUserId, CancellationToken ct = default);

    /// <summary>Admin hepsini, öğretmen yalnız kendi sorularını görür; en yeni önce.</summary>
    Task<QuestionReportPageDto> ListAsync(int userId, bool isAdmin, QuestionReportStatus status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Kayıt yoksa ya da çağıranın sorusu değilse false.</summary>
    Task<bool> MarkReviewedAsync(int reportId, int userId, bool isAdmin, CancellationToken ct = default);
}

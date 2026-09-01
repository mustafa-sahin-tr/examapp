using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Questions;

/// <summary>Question/passage reads. Split out of the former god-class <c>QuestionService</c>.</summary>
public interface IQuestionQueryService
{
    Task<QuestionDto?> GetQuestionById(int id);
    Task<List<PassageDto>> GetLastTenPassages();
    Task<List<QuestionDto>> GetQuestionByTestId(int testid);
}

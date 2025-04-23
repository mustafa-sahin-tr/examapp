using System;

namespace ExamApp.Api.Services.Interfaces;

public interface IQuestionService
{
    Task<QuestionDto?> GetQuestionById(int id);

    Task<List<PassageDto>> GetLastTenPassages();

    Task<List<QuestionDto>> GetQuestionByTestId(int testid);
}

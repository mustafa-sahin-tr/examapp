using System;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IQuestionService
{
    Task<QuestionDto?> GetQuestionById(int id);

    Task<List<PassageDto>> GetLastTenPassages();

    Task<List<QuestionDto>> GetQuestionByTestId(int testid);
    Task<QuestionSavedDto> CreateOrUpdateQuestion(QuestionDto questionDto);

    Task<ResponseBaseDto> SaveBulkQuestion(BulkQuestionCreateDto soruDto);

    Task<ResponseBaseDto> UpdateCorrectAnswer(
        int questionId,
        int correctAnswerId,
        int? subjectId = null,
        int? topicId = null,
        int? subTopicId = null,
        int[]? subTopicIds = null);

    Task<ResponseBaseDto> RemoveQuestionFromTest(int testId, int questionId);

    Task<ResponseBaseDto> ResizeQuestionImage(int questionId, double scale);
}

using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Questions;

/// <summary>Correct-answer / taxonomy edits and test membership for an existing question.
/// Split out of the former god-class <c>QuestionService</c>.</summary>
public interface IQuestionClassificationService
{
    Task<ResponseBaseDto> UpdateCorrectAnswer(int questionId, int correctAnswerId);

    Task<ResponseBaseDto> UpdateQuestionClassification(
        int questionId,
        int? subjectId = null,
        int? topicId = null,
        int? subTopicId = null,
        int[]? subTopicIds = null,
        string? classificationSourceStr = null,
        int? difficulty = null);

    Task<ResponseBaseDto> RemoveQuestionFromTest(int testId, int questionId);
}

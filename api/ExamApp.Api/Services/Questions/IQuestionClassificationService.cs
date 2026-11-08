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
        int? difficulty = null,
        int? classifierRevision = null);

    Task<ResponseBaseDto> RemoveQuestionFromTest(int testId, int questionId);

    /// <summary>
    /// Issue #590: sınıflandırıcının görseli olmayan soru için okuduğu düz metin (<see cref="QuestionClassificationText"/>);
    /// soru yoksa null.
    /// </summary>
    /// <summary>Issue #778: çözüm ekranı düzeni; soru yoksa false.</summary>
    Task<bool> SetLayoutChoiceAsync(int questionId, string? layoutChoice, CancellationToken ct = default);

    Task<string?> GetClassificationTextAsync(int questionId, CancellationToken ct = default);
}

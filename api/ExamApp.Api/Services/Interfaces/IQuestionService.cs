using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IQuestionService
{
    /// <param name="actingUserId">issue #287 H1: &gt; 0 ise yeni sorular bu kullanıcıya damgalanır (Question.CreateUserId → sahiplik).</param>
    /// <param name="isAdmin">issue #573: var olan kapsamın tür/yönergesini sahibi olmayan admin de değiştirebilir.</param>
    /// <param name="solutionActor">issue #580: çözüm onayı için çağıran (Admin/QuestionTrainer onaylayabilir); null = onaylayamaz, kimliksiz.</param>
    Task<QuestionSavedDto> CreateOrUpdateQuestion(QuestionDto questionDto, int actingUserId = 0, bool isAdmin = false,
        ExamApp.Api.Services.Questions.SolutionActor? solutionActor = null);

    /// <param name="actingUserId">issue #287 H1: &gt; 0 ise yeni sorular bu kullanıcıya damgalanır (Question.CreateUserId → sahiplik).</param>
    /// <param name="solutionActor">issue #580: çözüm onayı için çağıran; null = onaylayamaz, kimliksiz.</param>
    Task<ResponseBaseDto> SaveBulkQuestion(BulkQuestionCreateDto soruDto, int actingUserId = 0,
        ExamApp.Api.Services.Questions.SolutionActor? solutionActor = null);

    Task<StudyPageAttachImageResponseDto> AttachImageToStudyPage(StudyPageAttachImageDto request);

    Task<ResponseBaseDto> ResizeQuestionImage(int questionId, double scale);
}

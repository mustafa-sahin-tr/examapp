using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IQuestionService
{
    /// <param name="actingUserId">issue #287 H1: &gt; 0 ise yeni sorular bu kullanıcıya damgalanır (Question.CreateUserId → sahiplik).</param>
    Task<QuestionSavedDto> CreateOrUpdateQuestion(QuestionDto questionDto, int actingUserId = 0);

    /// <param name="actingUserId">issue #287 H1: &gt; 0 ise yeni sorular bu kullanıcıya damgalanır (Question.CreateUserId → sahiplik).</param>
    Task<ResponseBaseDto> SaveBulkQuestion(BulkQuestionCreateDto soruDto, int actingUserId = 0);

    Task<StudyPageAttachImageResponseDto> AttachImageToStudyPage(StudyPageAttachImageDto request);

    Task<ResponseBaseDto> ResizeQuestionImage(int questionId, double scale);
}

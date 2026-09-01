using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IQuestionService
{
    Task<QuestionSavedDto> CreateOrUpdateQuestion(QuestionDto questionDto);

    Task<ResponseBaseDto> SaveBulkQuestion(BulkQuestionCreateDto soruDto);

    Task<StudyPageAttachImageResponseDto> AttachImageToStudyPage(StudyPageAttachImageDto request);

    Task<ResponseBaseDto> ResizeQuestionImage(int questionId, double scale);
}

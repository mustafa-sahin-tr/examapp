using ExamApp.Api.Services.Entitlements;
using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IQuestionService
{
    /// <param name="actingUserId">issue #287 H1: &gt; 0 ise yeni sorular bu kullanıcıya damgalanır (Question.CreateUserId → sahiplik).</param>
    /// <param name="isAdmin">issue #573: var olan kapsamın tür/yönergesini sahibi olmayan admin de değiştirebilir.</param>
    Task<QuestionSavedDto> CreateOrUpdateQuestion(QuestionDto questionDto, int actingUserId = 0, bool isAdmin = false, EntitlementSubject? classificationOwner = null);

    /// <param name="actingUserId">issue #287 H1: &gt; 0 ise yeni sorular bu kullanıcıya damgalanır (Question.CreateUserId → sahiplik).</param>
    /// <param name="classificationOwner">
    /// Issue #1031: yeni soruların AI sınıflandırması bu kullanıcının planına sayılır; sınır doluysa soru sınıflandırıcıya gönderilmez
    /// (<see cref="QuestionSavedDto.ClassificationSkipped"/>). Null: sayılmaz (kitap / hızlı ekleme işleri, muaf rollerin ekranları).
    /// </param>
    Task<ResponseBaseDto> SaveBulkQuestion(BulkQuestionCreateDto soruDto, int actingUserId = 0, EntitlementSubject? classificationOwner = null);

    /// <summary>
    /// Issue #1087: sahibin planında yapay zekâ sınıflandırması yok (Basic): konuyu kendisi seçer, sınıflandırıcı çağrılmaz.
    /// Sahip verilmemişse (iç aktarım yolları) false.
    /// </summary>
    Task<bool> RequiresManualTopicAsync(EntitlementSubject? owner, CancellationToken ct = default);

    Task<StudyPageAttachImageResponseDto> AttachImageToStudyPage(StudyPageAttachImageDto request);

    Task<ResponseBaseDto> ResizeQuestionImage(int questionId, double scale);
}

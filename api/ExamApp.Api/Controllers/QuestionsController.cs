using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

[ApiController]
[Route("api/questions")]
// issue #287: tüm uçlar öğretmen (soru bankası/aktarım) yeteneği — Teacher rolündeki çağıranın hesabı onaylı olmalı.
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class QuestionsController : BaseController
{
    private readonly IMinIoService _minioService;
    private readonly ImageHelper _imageHelper;

    private readonly IQuestionService _questionService;
    private readonly IQuestionQueryService _questionQuery;
    private readonly IQuestionClassificationService _questionClassification;
    private readonly IClassifierCacheService _classifierCache;

    // Client'a dönen tüm metinler bu sözlükten gelir (issue #184). Aktif dil, istek kültürünü
    // ayarlayan RequestLocalization middleware'inden (#181) okunur.
    private readonly IStringLocalizer<Messages> _localizer;

    public QuestionsController(
        IMinIoService minioService,
        ImageHelper imageHelper,
        IQuestionService questionService,
        IQuestionQueryService questionQuery,
        IQuestionClassificationService questionClassification,
        IClassifierCacheService classifierCache,
        IStringLocalizer<Messages> localizer)
        : base()
    {
        _localizer = localizer;
        _minioService = minioService;
        _imageHelper = imageHelper;
        _questionService = questionService;
        _questionQuery = questionQuery;
        _questionClassification = questionClassification;
        _classifierCache = classifierCache;
    }

    // GET /api/questions/classifier-cache — the Gemini cached-content pointer the
    // classifier (BadgeService) should use right now. Service-to-service.
    [HttpGet("classifier-cache")]
    [Authorize]
    public async Task<IActionResult> GetClassifierCachePointer(CancellationToken ct)
    {
        var (cachedContentName, model) = await _classifierCache.GetActivePointerAsync(ct);
        return Ok(new { cachedContentName, model });
    }

    // 🟢 GET /api/questions/{id} - ID ile Soru Çekme
    [HttpGet("{id}")]
    public async Task<IActionResult> GetQuestionById(int id)
    {
        var response = await _questionQuery.GetQuestionById(id);
        if (response == null)
        {
            return NotFound(new { message = _localizer["questions.notFound"].Value });
        }
        return Ok(response);
    }

    [HttpGet("passages")]
    public async Task<IActionResult> GetLastTenPassages()
    {
        var passages = await _questionQuery.GetLastTenPassages();
        return Ok(passages);
    }

    // 🟢 GET /api/questions/{id} - ID ile Soru Çekme
    [HttpGet("bytest/{testid}")]
    public async Task<IActionResult> GetQuestionByTestId(int testid)
    {
        var questionList = await _questionQuery.GetQuestionByTestId(testid);
        return Ok(questionList);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateOrUpdateQuestion([FromBody] QuestionDto questionDto)
    {
        var response = await _questionService.CreateOrUpdateQuestion(questionDto);
        if (response == null)
        {
            return BadRequest(new { message = _localizer["questions.saveFailed"].Value });
        }
        return Ok(response);
    }

    [HttpPost("save")]
    public async Task<IActionResult> KaydetSoruSeti([FromBody] BulkQuestionCreateDto soruDto)
    {
        if (soruDto == null)
        {
            return BadRequest(_localizer["common.invalidData"].Value);
        }

        var reponse = await _questionService.SaveBulkQuestion(soruDto);
        if (reponse == null || !reponse.Success)
        {
            return BadRequest(_localizer["questions.bulk.saveFailed"].Value);
        }
        return Ok(reponse);
    }

    [HttpPost("attach-study-page")]
    public async Task<IActionResult> AddToStudyPage([FromBody] StudyPageAttachImageDto request)
    {
        var response = await _questionService.AttachImageToStudyPage(request);
        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    [HttpPut("{questionId}/correct-answer")]
    [Authorize]
    public async Task<IActionResult> UpdateCorrectAnswer(int questionId, [FromBody] UpdateCorrectAnswerDto request)
    {
        if (request == null || request.CorrectAnswerId <= 0)
        {
            return BadRequest(new { message = _localizer["questions.classification.invalidCorrectAnswerId"].Value });
        }

        var response = await _questionClassification.UpdateCorrectAnswer(
            questionId,
            request.CorrectAnswerId
        );
        if (request.Scale != 1)
        {
            response = await _questionService.ResizeQuestionImage(questionId, request.Scale);
        }

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    [HttpPut("{questionId}/classification")]
    [Authorize]
    public async Task<IActionResult> UpdateQuestionClassification(int questionId, [FromBody] UpdateQuestionClassificationDto request)
    {
        if (request == null)
        {
            return BadRequest(new { message = _localizer["questions.classification.invalidData"].Value });
        }

        var response = await _questionClassification.UpdateQuestionClassification(
            questionId,
            request.SubjectId,
            request.TopicId,
            request.SubTopicId,
            request.SubTopicIds,
            request.ClassificationSource,
            request.Difficulty
        );

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    // GET /api/questions/{id}/image?variant=v1|v2 — raw question image bytes.
    // Used by BadgeService's classifier (service-to-service) so MinIO access stays in this API.
    [HttpGet("{id}/image")]
    [Authorize]
    public async Task<IActionResult> GetQuestionImage(int id, [FromQuery] string variant = "v1")
    {
        var question = await _questionQuery.GetQuestionById(id);
        if (question == null || string.IsNullOrWhiteSpace(question.ImageUrl))
        {
            return NotFound(new { message = _localizer["questions.imageNotFound"].Value });
        }

        var imageUrl = question.ImageUrl;
        if (string.Equals(variant, "v2", StringComparison.OrdinalIgnoreCase))
        {
            imageUrl = Regex.Replace(imageUrl, @"question\.jpg$", "question-v2.jpg", RegexOptions.IgnoreCase);
        }

        var stream = await _minioService.GetFileStreamAsync(imageUrl);
        if (stream == null)
        {
            return NotFound(new { message = _localizer["questions.imageNotFoundInStorage"].Value });
        }

        return File(stream, "image/jpeg");
    }

    [HttpDelete("test/{testId}/question/{questionId}")]
    [Authorize]
    public async Task<IActionResult> RemoveQuestionFromTest(int testId, int questionId)
    {
        var response = await _questionClassification.RemoveQuestionFromTest(testId, questionId);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}


//     [HttpDelete("{id}")]
//     [Authorize]
//     public async Task<IActionResult> DeleteQuestion(int id)
//     {
//         try
//         {
//             var question = await _context.Questions
//                 .Include(q => q.Answers)
//                 .FirstOrDefaultAsync(q => q.Id == id);

//             if (question == null)
//             {
//                 return NotFound(new { message = "Soru bulunamadı!" });
//             }

//             _context.Answers.RemoveRange(question.Answers);
//             _context.Questions.Remove(question);
//             await _context.SaveChangesAsync();

//             return Ok(new { message = "Soru başarıyla silindi!" });
//         }
//         catch (Exception ex)
//         {
//             return BadRequest(new { error = ex.Message });
//         }
//     }


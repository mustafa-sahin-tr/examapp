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
using Microsoft.AspNetCore.Http;
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
// issue #287: tüm uçlar öğretmen (soru bankası) yeteneği — Teacher rolündeki çağıranın hesabı onaylı olmalı.
// Bu policy TEK BAŞINA rol kısıtı DEĞİLDİR (Teacher olmayanı geçirir); her uçta ayrıca rol/rol-policy'si var
// (security review H1): yazma + okuma uçları Teacher/Admin, BadgeService'in çağırdığı uçlar Teacher/Admin/servis,
// classifier-cache yalnızca Admin/servis. Öğrenci/veli hiçbir uca erişemez (UI'da öğrencinin kullandığı soru ucu yok;
// test çözme akışı api/worksheet/test-instance üzerinden).
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class QuestionsController : BaseController
{
    private readonly IMinIoService _minioService;
    private readonly ImageHelper _imageHelper;

    private readonly IQuestionService _questionService;
    private readonly IQuestionQueryService _questionQuery;
    private readonly IQuestionClassificationService _questionClassification;
    private readonly IClassifierCacheService _classifierCache;
    private readonly IQuestionOwnershipGuard _ownership;

    private const string AuthoringRoles = "Teacher,Admin";

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
        IStringLocalizer<Messages> localizer,
        IQuestionOwnershipGuard ownership)
        : base()
    {
        _ownership = ownership;
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
    [Authorize(Policy = QuestionAccessPolicies.AdminOrService)]
    public async Task<IActionResult> GetClassifierCachePointer(CancellationToken ct)
    {
        var (cachedContentName, model) = await _classifierCache.GetActivePointerAsync(ct);
        return Ok(new { cachedContentName, model });
    }

    // 🟢 GET /api/questions/{id} - ID ile Soru Çekme
    [HttpGet("{id}")]
    [Authorize(Roles = AuthoringRoles)]
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
    [Authorize(Roles = AuthoringRoles)]
    public async Task<IActionResult> GetLastTenPassages()
    {
        var passages = await _questionQuery.GetLastTenPassages();
        return Ok(passages);
    }

    // 🟢 GET /api/questions/{id} - ID ile Soru Çekme
    [HttpGet("bytest/{testid}")]
    [Authorize(Roles = AuthoringRoles)]
    public async Task<IActionResult> GetQuestionByTestId(int testid)
    {
        var questionList = await _questionQuery.GetQuestionByTestId(testid);
        return Ok(questionList);
    }

    [HttpPost]
    [Authorize(Roles = AuthoringRoles)]
    public async Task<IActionResult> CreateOrUpdateQuestion([FromBody] QuestionDto questionDto)
    {
        var user = await GetAuthenticatedUserAsync();
        // issue #287 H1: güncellemede soru sahibi, yeni soruyu teste eklerken test sahibi olmalı (admin muaf).
        var denied = questionDto.Id > 0
            ? await DenyUnlessQuestionOwnerAsync(questionDto.Id, user.Id)
            : questionDto.TestId is > 0
                ? await DenyUnlessWorksheetOwnerAsync(questionDto.TestId.Value, user.Id)
                : null;
        if (denied != null)
            return denied;

        var response = await _questionService.CreateOrUpdateQuestion(questionDto, user.Id);
        if (response == null)
        {
            return BadRequest(new { message = _localizer["questions.saveFailed"].Value });
        }
        return Ok(response);
    }

    [HttpPost("save")]
    [Authorize(Roles = AuthoringRoles)]
    public async Task<IActionResult> KaydetSoruSeti([FromBody] BulkQuestionCreateDto soruDto)
    {
        if (soruDto == null)
        {
            return BadRequest(_localizer["common.invalidData"].Value);
        }

        var user = await GetAuthenticatedUserAsync();
        // issue #287 H1: sorular bir teste ekleniyorsa test sahibi olmalı (admin muaf).
        if (soruDto.Header?.TestId is int testId && testId > 0
            && await DenyUnlessWorksheetOwnerAsync(testId, user.Id) is { } denied)
            return denied;

        var reponse = await _questionService.SaveBulkQuestion(soruDto, user.Id);
        if (reponse == null || !reponse.Success)
        {
            return BadRequest(_localizer["questions.bulk.saveFailed"].Value);
        }
        return Ok(reponse);
    }

    [HttpPost("attach-study-page")]
    [Authorize(Roles = AuthoringRoles)] // yalnızca görsel yükler; bir kaynağa bağlı değil → rol yeterli
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
    [Authorize(Roles = AuthoringRoles)]
    public async Task<IActionResult> UpdateCorrectAnswer(int questionId, [FromBody] UpdateCorrectAnswerDto request)
    {
        if (request == null || request.CorrectAnswerId <= 0)
        {
            return BadRequest(new { message = _localizer["questions.classification.invalidCorrectAnswerId"].Value });
        }

        var user = await GetAuthenticatedUserAsync();
        if (await DenyUnlessQuestionOwnerAsync(questionId, user.Id) is { } denied)
            return denied;

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

    // BadgeService GeminiQuestionClassifier servis hesabıyla çağırır (issue #287 H1: servis sahiplik kontrolünden muaf).
    [HttpPut("{questionId}/classification")]
    [Authorize(Policy = QuestionAccessPolicies.TeacherAdminOrService)]
    public async Task<IActionResult> UpdateQuestionClassification(int questionId, [FromBody] UpdateQuestionClassificationDto request)
    {
        if (request == null)
        {
            return BadRequest(new { message = _localizer["questions.classification.invalidData"].Value });
        }

        if (!IsServiceAccount)
        {
            var user = await GetAuthenticatedUserAsync();
            if (await DenyUnlessQuestionOwnerAsync(questionId, user.Id) is { } denied)
                return denied;
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
    [Authorize(Policy = QuestionAccessPolicies.TeacherAdminOrService)]
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
    [Authorize(Roles = AuthoringRoles)]
    public async Task<IActionResult> RemoveQuestionFromTest(int testId, int questionId)
    {
        // issue #287 H1: yalnızca test sahibi (admin muaf) testinden soru çıkarabilir.
        var user = await GetAuthenticatedUserAsync();
        if (await DenyUnlessWorksheetOwnerAsync(testId, user.Id) is { } denied)
            return denied;

        var response = await _questionClassification.RemoveQuestionFromTest(testId, questionId);

        if (!response.Success)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    // ---------------- issue #287 H1: kaynak sahipliği ----------------

    /// <summary>Soru yoksa 404, sahibi değilse (admin değil) 403; izinliyse null.</summary>
    private async Task<IActionResult?> DenyUnlessQuestionOwnerAsync(int questionId, int userId) =>
        await _ownership.CanModifyQuestionAsync(questionId, userId, IsAdmin, HttpContext.RequestAborted) switch
        {
            QuestionAccessResult.NotFound => NotFound(new { success = false, message = _localizer["questions.notFound"].Value }),
            QuestionAccessResult.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = _localizer["questions.forbidden"].Value }),
            _ => null
        };

    /// <summary>Test yoksa 404, sahibi değilse (admin değil) 403; izinliyse null.</summary>
    private async Task<IActionResult?> DenyUnlessWorksheetOwnerAsync(int worksheetId, int userId) =>
        await _ownership.CanModifyWorksheetAsync(worksheetId, userId, IsAdmin, HttpContext.RequestAborted) switch
        {
            QuestionAccessResult.NotFound => NotFound(new { success = false, message = _localizer["questions.testNotFound"].Value }),
            QuestionAccessResult.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new { success = false, message = _localizer["questions.testForbidden"].Value }),
            _ => null
        };
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


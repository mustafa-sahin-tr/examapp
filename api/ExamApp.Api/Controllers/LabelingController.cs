using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// İşaretleme ekranının kaynakları (Model / Claude / Manuel). Gateway: <c>/api/exam/labeling/*</c>. Yalnız eğitim verisi
/// üretebilen roller (Admin, QuestionTrainer) — gateway'deki <c>send-to-fix</c> kuralıyla aynı küme. QuestionTrainer realm
/// rolü öğretmen askıya alınınca kalkmadığı için Teacher rolündeki çağıran ayrıca onaylı ve askıda olmayan öğretmen
/// olmalı (<see cref="ApprovedTeacherPolicies.TeacherCapability"/>; Admin muaf) — bu uçlar şirket bütçesi harcar.
/// </summary>
[ApiController]
[Route("api/labeling")]
[Authorize(Roles = "Admin,QuestionTrainer")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LabelingController : BaseController
{
    private readonly ILabelingService _service;
    private readonly IStringLocalizer<Messages> _localizer;

    public LabelingController(ILabelingService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>İzinli Claude modelleri ve 1M token fiyatları.</summary>
    [HttpGet("ai-models")]
    public IActionResult GetAiModels() => Ok(_service.GetAiModels());

    /// <summary>Sayfanın (ham görsel SHA-256, küçük harf hex) kayıtlı tüm kaynakları; kayıt yoksa 404.</summary>
    [HttpGet("pages/{sha256}")]
    public async Task<IActionResult> GetPage(string sha256, CancellationToken ct)
    {
        try
        {
            var page = await _service.GetPageAsync(sha256, ct);
            return page is null ? NotFound(Error("labeling.pageNotFound")) : Ok(page);
        }
        catch (LabelingValidationException ex)
        {
            return BadRequest(Error(ex.Message));
        }
    }

    /// <summary>
    /// Claude ile soru/şık tespiti. Kayıtlı sonuç varsa (ve <c>force</c> değilse) API'ye gidilmez: <c>origin=cached</c>.
    /// 400 geçersiz görsel/model; 429 saatlik kota; 502 Claude yanıtı kullanılamadı; 503 API anahtarı yok.
    /// </summary>
    [HttpPost("ai-detect")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> DetectWithAi([FromBody] LabelingAiDetectRequestDto request, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.DetectWithAiAsync(request, CurrentActor(), ct));
        }
        catch (LabelingValidationException ex)
        {
            return BadRequest(Error(ex.Message));
        }
        catch (LabelingRateLimitException ex)
        {
            if (ex.RetryAfter is { } retryAfter)
            {
                Response.Headers.RetryAfter =
                    Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }
            return StatusCode(StatusCodes.Status429TooManyRequests, ex.Budget
                ? Error("labeling.budgetExceeded", "LabelingBudgetExceeded")
                : Error("labeling.rateLimited", "LabelingRateLimited"));
        }
        catch (LabelingAiUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("labeling.aiUnavailable", "LabelingAiUnavailable"));
        }
        catch (LabelingAiException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, Error("labeling.aiFailed", "LabelingAiFailed"));
        }
    }

    /// <summary>Claude prompt'u: etkin sürüm, koddaki varsayılanlar ve son sürümler.</summary>
    [HttpGet("prompt")]
    public async Task<IActionResult> GetPrompt(CancellationToken ct) => Ok(await _service.GetPromptStateAsync(ct));

    /// <summary>Prompt'u yeni sürüm olarak kaydeder; sonraki Claude çağrıları bunu kullanır (eski sonuçlar kayıttan dönmez).</summary>
    [HttpPut("prompt")]
    public async Task<IActionResult> SavePrompt([FromBody] LabelingSavePromptRequestDto request, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.SavePromptAsync(request, CurrentActor(), ct));
        }
        catch (LabelingValidationException ex)
        {
            return BadRequest(Error(ex.Message));
        }
    }

    /// <summary>Eğitime gönderilen son hal (Manuel) + o anki YOLO tahmini (Model). Güncel sayfa kaydını döner.</summary>
    [HttpPut("pages/manual")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> SaveManual([FromBody] LabelingSaveManualRequestDto request, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.SaveManualAsync(request, CurrentActor(), ct));
        }
        catch (LabelingValidationException ex)
        {
            return BadRequest(Error(ex.Message));
        }
    }

    /// <summary>
    /// Sayfadaki soruları teste bağlamadan soru bankasına kaydeder (issue #470); sahibi çağıran, sınıflandırma
    /// <c>QuestionCreatedEvent</c> ile başlar. Soru yazmak öğretmen yeteneği olduğundan sınıf düzeyindeki rollere ek olarak
    /// Teacher veya Admin gerekir (QuestionTrainer tek başına yetmez). 400 geçersiz kutu / doğru şık eksik; 409 sayfa daha
    /// önce kaydedildi (<c>force</c> ile yeniden kaydedilir) ya da şu an kaydediliyor; 429 saatlik kayıt sınırı; 500 kayıt
    /// başarısız.
    /// </summary>
    [HttpPost("pages/bank")]
    [Authorize(Roles = "Teacher,Admin")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> SaveToBank(
        [FromBody] LabelingBankSaveRequestDto request, [FromServices] ILabelingBankService bank, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Error("labeling.bank.userNotResolved"));

        try
        {
            return Ok(await bank.SaveToBankAsync(request, CurrentActor(), user.Id, ct));
        }
        catch (LabelingValidationException ex)
        {
            return BadRequest(Error(ex.Message));
        }
        catch (LabelingBankDuplicateException ex)
        {
            return Conflict(new
            {
                message = _localizer["labeling.bank.alreadySaved"].Value,
                errorCode = "LabelingPageAlreadyInBank",
                existing = ex.Existing,
            });
        }
        catch (LabelingRateLimitException ex)
        {
            if (ex.RetryAfter is { } retryAfter)
            {
                Response.Headers.RetryAfter =
                    Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }
            return StatusCode(StatusCodes.Status429TooManyRequests, Error("labeling.bank.rateLimited", "LabelingBankRateLimited"));
        }
        catch (LabelingBankSaveException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, Error("labeling.bank.saveFailed", "LabelingBankSaveFailed"));
        }
    }

    private LabelingActor CurrentActor() =>
        new(KeyCloakId, User.FindFirstValue("name") ?? User.FindFirstValue("preferred_username"));

    private object Error(string messageKey, string? errorCode = null) =>
        new { message = _localizer[messageKey].Value, errorCode };
}

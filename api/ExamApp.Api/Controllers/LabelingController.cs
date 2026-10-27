using System;
using System.Globalization;
using System.Linq;
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
            return RateLimited(ex);
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

    /// <summary>
    /// Issue #579: etkinlik bloklarının taslak etkileşim planı (blok başına tipine göre bir Claude çağrısı, paralel). Kayıtlı
    /// sonuç (aynı kırpma + tip + model + o tipin prompt'u) ücretsiz döner (<c>origin=cached</c>). 400 geçersiz istek; 429 saatlik
    /// kota / günlük bütçe (canlı çağrı gereken blok sayısı baştan sayılır); 503 API anahtarı yok. Tek bloğun Claude hatası
    /// yalnız o bloğu <c>status=error</c> yapar.
    /// </summary>
    [HttpPost("activity-structure")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> ExtractActivityStructure(
        [FromBody] ActivityStructureRequestDto request, [FromServices] IActivityStructureService activities, CancellationToken ct)
    {
        try
        {
            var response = await activities.ExtractAsync(request, CurrentActor(), ct);
            return Ok(new ActivityStructureResponseDto(response.Results.Select(Localize).ToList()));
        }
        catch (LabelingValidationException ex)
        {
            return BadRequest(Error(ex.Message));
        }
        catch (LabelingRateLimitException ex)
        {
            return RateLimited(ex);
        }
        catch (LabelingAiUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("labeling.aiUnavailable", "LabelingAiUnavailable"));
        }
    }

    /// <summary>Issue #579: tip başına etkinlik talimatları — etkin sürüm, koddaki varsayılan ve son sürümler.</summary>
    [HttpGet("activity-prompts")]
    public async Task<IActionResult> GetActivityPrompts([FromServices] IActivityStructureService activities, CancellationToken ct) =>
        Ok(await activities.GetPromptsAsync(ct));

    /// <summary>Issue #579: tipin talimatını yeni sürüm olarak kaydeder (içerik aynıysa sürüm açılmaz); etkin sürümü döner.</summary>
    [HttpPut("activity-prompts/{interactionType}")]
    public async Task<IActionResult> SaveActivityPrompt(
        string interactionType, [FromBody] ActivitySavePromptRequestDto request,
        [FromServices] IActivityStructureService activities, CancellationToken ct)
    {
        try
        {
            return Ok(await activities.SavePromptAsync(interactionType, request, CurrentActor(), ct));
        }
        catch (LabelingValidationException ex)
        {
            return BadRequest(Error(ex.Message));
        }
    }

    /// <summary>
    /// Issue #580: kitabın cevap anahtarı sayfasını Claude ile okur ve (kitap, dosya sayfası) başına saklar; aynı görsel + model +
    /// <c>answerKey</c> talimatı için kayıtlı okuma ücretsiz döner (<c>origin=cached</c>). 400 geçersiz istek / sayfa cevap anahtarı
    /// aralığında değil (<c>LabelingNotAnswerKeyPage</c>); 404 kitap yok; 409 sayfada öğretmen düzeltmesi var ve <c>force</c> istendi ama
    /// <c>confirmOverwrite</c> yok (<c>LabelingAnswerKeyHasEdits</c>); 429 kota/bütçe; 502 Claude okuyamadı; 503 API anahtarı yok.
    /// Düzeltmesi olan sayfa <c>force</c>'suz istekte yeniden okunmaz (<c>stale</c>).
    /// </summary>
    [HttpPost("answer-key/read")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public Task<IActionResult> ReadAnswerKey(
        [FromBody] AnswerKeyReadRequestDto request, [FromServices] IActivitySolutionService solutions, CancellationToken ct) =>
        HandleSolutionAsync(async () => Ok(await solutions.ReadAnswerKeyAsync(request, CurrentActor(), ct)),
            "labeling.answerKey.aiFailed");

    /// <summary>Issue #580: anahtar maddesini düzeltir (okuma hatası). 404 madde yok; 400 geçersiz alan.</summary>
    [HttpPut("answer-key/entries/{id:int}")]
    public Task<IActionResult> UpdateAnswerKeyEntry(
        int id, [FromBody] AnswerKeyEntryUpdateRequestDto request, [FromServices] IActivitySolutionService solutions,
        CancellationToken ct) =>
        HandleSolutionAsync(async () => await solutions.UpdateEntryAsync(id, request, CurrentActor(), ct) is { } entry
            ? Ok(entry)
            : NotFound(Error("labeling.answerKey.entryNotFound")));

    /// <summary>Issue #580: anahtar maddesini siler. 204; 404 madde yok.</summary>
    [HttpDelete("answer-key/entries/{id:int}")]
    public async Task<IActionResult> DeleteAnswerKeyEntry(int id, [FromServices] IActivitySolutionService solutions, CancellationToken ct) =>
        await solutions.DeleteEntryAsync(id, CurrentActor(), ct) ? NoContent() : NotFound(Error("labeling.answerKey.entryNotFound"));

    /// <summary>
    /// Issue #580: taslak etkinliğin çözümü — <c>mode=key</c> seçilen anahtar maddeleri (metin + görsel kırpma) + etkinlik kırpması,
    /// <c>mode=ai</c> yalnız etkinlik kırpması. Çözüm onay bekler (<c>status=pending</c>). Kayıtlı sonuç (aynı kırpma + plan + tip + mod +
    /// anahtar girdisi + model, o modun talimatıyla) ücretsiz döner. 400 geçersiz istek / plan; 429; 503. Claude hatası 200 + <c>error</c>.
    /// </summary>
    [HttpPost("activity-solution")]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public Task<IActionResult> SolveActivity(
        [FromBody] ActivitySolutionRequestDto request, [FromServices] IActivitySolutionService solutions, CancellationToken ct) =>
        HandleSolutionAsync(async () => Ok(Localize(await solutions.SolveAsync(request, CurrentActor(), ct))));

    /// <summary>
    /// Issue #580: kırpmanın kayıtlı çözüm taslakları (en son güncellenen önce, en çok 10) — yeniden açılışta geri yükleme.
    /// <c>cropSha256</c> zorunlu (#579 <c>activity-structure</c> yanıtındaki <c>cropSha256</c>).
    /// </summary>
    [HttpGet("activity-solution")]
    public Task<IActionResult> FindActivitySolutions(
        [FromQuery] string? cropSha256, [FromQuery] string? interactionType, [FromQuery] string? planHash,
        [FromServices] IActivitySolutionService solutions, CancellationToken ct) =>
        HandleSolutionAsync(async () =>
        {
            var list = await solutions.FindSolutionsAsync(cropSha256, interactionType, planHash, ct);
            return Ok(new ActivitySolutionListDto(list.Solutions.Select(Localize).ToList()));
        });

    /// <summary>
    /// Issue #580: çözümü düzeltir (taslak planına göre geçerli olmalı). <c>approve=false</c>: kaynak aynı, durum pending;
    /// <c>approve=true</c>: düzelten onaylar (kaynak Human). 404; 400 geçersiz çözüm.
    /// </summary>
    [HttpPut("activity-solution/{id:int}")]
    public Task<IActionResult> UpdateActivitySolution(
        int id, [FromBody] ActivitySolutionUpdateRequestDto request, [FromServices] IActivitySolutionService solutions,
        CancellationToken ct) =>
        HandleSolutionAsync(async () => await solutions.UpdateSolutionAsync(id, request, CurrentActor(), ct) is { } dto
            ? Ok(Localize(dto))
            : NotFound(Error("labeling.solution.notFound")));

    /// <summary>
    /// Issue #580: çözümü onaylar — onaylayan kullanıcı ve zaman kaydedilir. Yalnız Admin + QuestionTrainer (sınıf düzeyi; diğer
    /// roller 403). 404; 400 geçersiz çözüm onaylanamaz.
    /// </summary>
    [HttpPost("activity-solution/{id:int}/approve")]
    public Task<IActionResult> ApproveActivitySolution(int id, [FromServices] IActivitySolutionService solutions, CancellationToken ct) =>
        HandleSolutionAsync(async () => await solutions.ApproveSolutionAsync(id, CurrentActor(), ct) is { } dto
            ? Ok(Localize(dto))
            : NotFound(Error("labeling.solution.notFound")));

    private async Task<IActionResult> HandleSolutionAsync(Func<Task<IActionResult>> action, string aiFailedKey = "labeling.solution.aiFailed")
    {
        try
        {
            return await action();
        }
        catch (LabelingValidationException ex)
        {
            return BadRequest(Error(ex.Message));
        }
        catch (LabelingNotAnswerKeyPageException ex)
        {
            return BadRequest(Error(ex.Message, "LabelingNotAnswerKeyPage"));
        }
        catch (LabelingNotFoundException ex)
        {
            return NotFound(Error(ex.Message));
        }
        catch (LabelingAnswerKeyHasEditsException ex)
        {
            return Conflict(Error(ex.Message, "LabelingAnswerKeyHasEdits"));
        }
        catch (LabelingRateLimitException ex)
        {
            return RateLimited(ex);
        }
        catch (LabelingAiUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Error("labeling.aiUnavailable", "LabelingAiUnavailable"));
        }
        catch (LabelingAiException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, Error(aiFailedKey, "LabelingAiFailed"));
        }
    }

    private LabelingActivitySolutionDto Localize(LabelingActivitySolutionDto dto) => dto with
    {
        Error = dto.Error is null ? null : _localizer[dto.Error].Value,
        ValidationErrors = dto.ValidationErrors.Select(key => _localizer[key].Value).ToList(),
    };

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
    /// Teacher veya Admin gerekir (QuestionTrainer tek başına yetmez). 400 geçersiz kutu / doğru şık eksik / sınıf eksik ya da bilinmiyor (#565) /
    /// kitap bulunamadı ya da kitap-sayfa eksik (#577); 409 sayfa daha önce kaydedildi (<c>force</c> ile yeniden kaydedilir) ya da
    /// şu an kaydediliyor, ya da aynı kırpma bankada var (<c>LabelingCropAlreadyInBank</c>, #577); 429 saatlik kayıt sınırı; 500 kayıt
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
        catch (LabelingCropDuplicateException ex)
        {
            // Issue #577: aynı kırpma bankada var (herhangi bir kitap/baskı); force ile yine de kaydedilir.
            return Conflict(new
            {
                message = _localizer["labeling.bank.cropAlreadySaved"].Value,
                errorCode = "LabelingCropAlreadyInBank",
                // Security M1: başkasının sorusu yalnız {questionIndex, otherOwner: true} (Admin hepsini görür).
                existing = ex.Existing.Select(e => e.ToResponse()).ToList(),
            });
        }
        catch (LabelingAnswerKeyPageException)
        {
            // Issue #577 (CR12): cevap anahtarı sayfası; force ile yine de kaydedilir.
            return Conflict(Error("labeling.bank.answerKeyPage", "LabelingAnswerKeyPage"));
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

    /// <summary>Claude kotası (#578/#579 ortak): 429 + Retry-After (en az 1 sn), bütçe / saatlik kota hata kodu.</summary>
    private ObjectResult RateLimited(LabelingRateLimitException ex)
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

    /// <summary>Servisin döndürdüğü anahtarlar (blok hatası, doğrulama ipuçları) → istek dilinde mesaj.</summary>
    private ActivityStructureResultDto Localize(ActivityStructureResultDto result) => result with
    {
        Error = result.Error is null ? null : _localizer[result.Error].Value,
        ValidationErrors = result.ValidationErrors.Select(key => _localizer[key].Value).ToList(),
    };

    private LabelingActor CurrentActor() =>
        new(KeyCloakId, User.FindFirstValue("name") ?? User.FindFirstValue("preferred_username"), IsAdmin);

    private object Error(string messageKey, string? errorCode = null) =>
        new { message = _localizer[messageKey].Value, errorCode };
}

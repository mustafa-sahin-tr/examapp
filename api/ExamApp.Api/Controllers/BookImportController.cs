using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1018 (epic #1017): kitaptan aktarım — PDF yükle, işleri listele, bir işi takip et. Yükleme #1087'den beri planın
/// <c>book-import</c> hakkına bağlı (Admin ve QuestionTrainer muaf; onaylı öğretmen policy'si). Kullanıcı yalnız kendi işlerini
/// görür, Admin hepsini; başkasının işi 404.
/// </summary>
[ApiController]
[Route("api/book-imports")]
// Issue #1032: demo işleri her öğretmene açık. Issue #1087: gerçek aktarım uçları rol yerine book-import planına bağlı (Admin ve
// QuestionTrainer muaf); yükleme aylık sınıra, yeniden deneme ve düzeltme yalnız "planda var mı"ya bakar.
[Authorize(Roles = "Admin,QuestionTrainer,Teacher")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class BookImportController : BaseController
{
    private readonly IBookImportService _service;
    private readonly IStringLocalizer<Messages> _localizer;

    public BookImportController(IBookImportService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    [HttpPost]
    [RequiresEntitlement(EntitlementFeatures.BookImport)]
    [RequestSizeLimit(BookImportService.MaxUploadBytesCeiling + 1_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = BookImportService.MaxUploadBytesCeiling + 1_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Start([FromForm] StartBookImportFormDto request, CancellationToken ct)
    {
        if (request?.File is not { Length: > 0 } file)
            return BadRequest(new { message = _localizer["bookImport.fileRequired"].Value });

        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });

        await using var content = file.OpenReadStream();
        var result = await _service.StartAsync(owner,
            new StartBookImportRequest(request.BookName ?? string.Empty, request.GradeId, request.SubjectId, file.FileName, file.Length, content), ct);

        return result.Error switch
        {
            StartBookImportError.None => Ok(result.Job),
            StartBookImportError.QuotaReached => QuotaReached(result.Quota),
            var error => BadRequest(new { code = $"bookImport.{Camel(error)}", message = _localizer[$"bookImport.{Camel(error)}"].Value }),
        };
    }

    /// <summary>
    /// Issue #1122: büyük PDF'i Cloudflare'in 100 MB istek sınırına takılmadan yüklemek için parçalı oturum açar. Kurallar tek istekli
    /// yüklemeyle aynı (ad, sınıf, ders, boyut, aylık hak); dönüşteki parça boyutuyla dosya bölünüp sırayla gönderilir.
    /// </summary>
    [HttpPost("uploads")]
    [RequiresEntitlement(EntitlementFeatures.BookImport)]
    public async Task<IActionResult> BeginUpload([FromBody] BeginBookImportUploadDto request, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });

        var result = await _service.BeginUploadAsync(owner,
            new BeginBookImportUploadRequest(request.BookName ?? string.Empty, request.GradeId, request.SubjectId, request.FileName,
                request.FileSizeBytes), ct);
        return result.Error switch
        {
            StartBookImportError.None => Ok(result.Upload),
            StartBookImportError.QuotaReached => QuotaReached(result.Quota),
            var error => BadRequest(new { code = $"bookImport.{Camel(error)}", message = _localizer[$"bookImport.{Camel(error)}"].Value }),
        };
    }

    /// <summary>Issue #1122: bir parça (gövde ham bayt, 1'den numaralı). Aynı parçayı yeniden göndermek üzerine yazar.</summary>
    [HttpPut("uploads/{id:guid}/parts/{partNumber:int}")]
    [RequiresEntitlement(EntitlementFeatures.BookImport)]
    [RequestSizeLimit(BookImportService.UploadPartSizeBytes + 1)]
    public async Task<IActionResult> UploadPart(Guid id, int partNumber, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        if (Request.ContentLength is not { } length || length <= 0 || length > BookImportService.UploadPartSizeBytes)
            return InvalidPart();

        using var content = new MemoryStream((int)length);
        await Request.Body.CopyToAsync(content, ct);
        return await _service.UploadPartAsync(owner, id, partNumber, content, ct) switch
        {
            BookImportUploadPartResult.Ok => NoContent(),
            BookImportUploadPartResult.NotFound => UploadNotFound(),
            BookImportUploadPartResult.NotPdf => BadRequest(new { code = "bookImport.notPdf", message = _localizer["bookImport.notPdf"].Value }),
            _ => InvalidPart(),
        };
    }

    /// <summary>Issue #1122: parçaları birleştirir ve aktarım işini başlatır (dönüş tek istekli yüklemeyle aynı).</summary>
    [HttpPost("uploads/{id:guid}/complete")]
    [RequiresEntitlement(EntitlementFeatures.BookImport)]
    public async Task<IActionResult> CompleteUpload(Guid id, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });

        var result = await _service.CompleteUploadAsync(owner, id, ct);
        return result.Error switch
        {
            CompleteBookImportUploadError.None => Ok(result.Job),
            CompleteBookImportUploadError.NotFound => UploadNotFound(),
            CompleteBookImportUploadError.QuotaReached => QuotaReached(result.Quota),
            CompleteBookImportUploadError.Incomplete => Conflict(new { code = "bookImport.uploadIncomplete", message = _localizer["bookImport.uploadIncomplete"].Value }),
            _ => BadRequest(new { code = "bookImport.notPdf", message = _localizer["bookImport.notPdf"].Value }),
        };
    }

    /// <summary>Issue #1122: yarım yüklemeyi bırakır; parçalar silinir.</summary>
    [HttpDelete("uploads/{id:guid}")]
    public async Task<IActionResult> AbortUpload(Guid id, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return await _service.AbortUploadAsync(owner, id, ct) ? NoContent() : UploadNotFound();
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<BookImportJobDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int take = 50, CancellationToken ct = default)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return Ok(await _service.ListAsync(owner, take, ct));
    }

    /// <summary>Issue #1032: örnek kitapla demonun durumu.</summary>
    [HttpGet("demo")]
    public async Task<IActionResult> GetDemo(CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return Ok(await _service.GetDemoAsync(owner, ct));
    }

    /// <summary>Issue #1032: demoyu başlatır; bitmemiş demo varsa ona döner. AI çağrısı ve hak kullanımı yok.</summary>
    [HttpPost("demo")]
    public async Task<IActionResult> StartDemo(CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        var result = await _service.StartDemoAsync(owner, ct);
        return result.Error switch
        {
            StartBookImportDemoError.None => Ok(result.Job),
            StartBookImportDemoError.LimitReached => Conflict(new { code = "bookImport.demoLimitReached", message = _localizer["bookImport.demoLimitReached"].Value }),
            _ => NotFound(new { code = "bookImport.demoUnavailable", message = _localizer["bookImport.demoUnavailable"].Value }),
        };
    }

    /// <summary>Issue #1032: öğretmenlere oynatılacak kaynak işi seçer; önceki seçim kalkar.</summary>
    [HttpPut("{id:guid}/demo-source")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetDemoSource(Guid id, [FromBody] SetBookImportDemoSourceDto request, CancellationToken ct) =>
        await _service.SetDemoSourceAsync(id, request.IsDemoSource, ct) switch
        {
            SetBookImportDemoSourceResult.Ok => NoContent(),
            SetBookImportDemoSourceResult.NotEligible => BadRequest(new { code = "bookImport.demoSourceNotEligible", message = _localizer["bookImport.demoSourceNotEligible"].Value }),
            _ => NotFound(),
        };

    /// <summary>
    /// Issue #1133: incelenmiş kitap sayfalarının eğitim verisi. <paramref name="dryRun"/> (varsayılan) yalnız özet döner;
    /// false iken sayfa görselleri + <c>manifest.json</c> ZIP olarak iner (question-detector
    /// <c>training/import_book_export.py</c> veri setine yazar). <paramref name="jobId"/> boşsa bütün kitap işleri.
    /// </summary>
    [HttpGet("training-export")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(BookImportTrainingSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> TrainingExport([FromServices] IBookImportTrainingExport export, [FromQuery] Guid? jobId,
        [FromQuery] bool dryRun = true, CancellationToken ct = default)
    {
        if (dryRun)
            return Ok(await export.SummarizeAsync(jobId, ct));
        // Sayfalar yüzlerce MB olabilir: geçici dosyaya yazılır, yanıt bitince silinir.
        var file = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await export.WriteZipAsync(jobId, file, ct);
            file.Position = 0;
            return File(file, "application/zip", $"book-import-training-{DateTime.UtcNow:yyyyMMdd-HHmm}.zip");
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    [HttpGet("quota")]
    public async Task<IActionResult> Quota(CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return Ok(await _service.GetQuotaAsync(owner, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        var job = await _service.GetAsync(owner, id, ct);
        return job is null ? NotFound(new { message = _localizer["bookImport.notFound"].Value }) : Ok(job);
    }

    /// <summary>Issue #1020: işin sayfa görseli (WebP); iş sahibine (ve Admin'e).</summary>
    [HttpGet("{id:guid}/pages/{filePage:int}")]
    public async Task<IActionResult> GetPage(Guid id, int filePage, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        var image = await _service.GetPageImageAsync(owner, id, filePage, ct);
        return image is null ? NotFound(new { message = _localizer["bookImport.notFound"].Value }) : File(image, "image/webp");
    }

    /// <summary>Issue #1021: kurulum onay ekranı (testler, cevap anahtarı, üniteler); kurulum yoksa 404.</summary>
    [HttpGet("{id:guid}/setup")]
    [ProducesResponseType(typeof(BookImportSetupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSetup(Guid id, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        var setup = await _service.GetSetupAsync(owner, id, ct);
        return setup is null ? NotFound(new { message = _localizer["bookImport.notFound"].Value }) : Ok(setup);
    }

    /// <summary>
    /// Issue #1021: kurulumu onayla — hak düşer, kitap kaydı yazılır, soru aktarımı başlar. 409: aynı adlı kitap var
    /// (<c>existingBook</c>; mevcut kitabı kullan ya da yeni ad), 429: aylık hak doldu.
    /// </summary>
    [HttpPost("{id:guid}/setup/confirm")]
    public async Task<IActionResult> ConfirmSetup(Guid id, [FromBody] ConfirmBookImportSetupDto request, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });

        var result = await _service.ConfirmSetupAsync(owner, id, request ?? new ConfirmBookImportSetupDto(), ct);
        return result.Error switch
        {
            ConfirmSetupError.None => Ok(result.Job),
            ConfirmSetupError.NotFound => NotFound(new { message = _localizer["bookImport.notFound"].Value }),
            ConfirmSetupError.InvalidState => Conflict(new { code = "bookImport.notAwaitingSetup", message = _localizer["bookImport.notAwaitingSetup"].Value }),
            ConfirmSetupError.NameTaken => Conflict(new
            {
                code = "bookImport.bookNameTaken",
                existingBook = result.ExistingBook,
                message = _localizer["bookImport.bookNameTaken"].Value,
            }),
            ConfirmSetupError.QuotaReached => QuotaReached(result.Quota),
            _ => BadRequest(new { code = result.Code, message = _localizer[result.Code ?? "bookImport.invalidSetup"].Value }),
        };
    }

    /// <summary>
    /// İşi iptal et (#1021 onay bekleyen, ör. cevap anahtarı bulunamadı; #1025 kuyrukta / çalışan / bekleyen / hatalı).
    /// <paramref name="deleteDrafts"/>: onaylanmamış taslak testler ve soruları da silinir.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromQuery] bool deleteDrafts = false, CancellationToken ct = default)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return await _service.CancelAsync(owner, id, deleteDrafts, ct) switch
        {
            CancelBookImportResult.Cancelled => NoContent(),
            CancelBookImportResult.NotFound => NotFound(new { message = _localizer["bookImport.notFound"].Value }),
            _ => Conflict(new { code = "bookImport.notCancellable", message = _localizer["bookImport.notCancellable"].Value }),
        };
    }

    /// <summary>Issue #1025: hatalı ya da takılmış işi kaldığı yerden yeniden dene.</summary>
    [HttpPost("{id:guid}/retry")]
    [RequiresEntitlement(EntitlementFeatures.BookImport, InPlanOnly = true)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return RetryResult(await _service.RetryAsync(owner, id, ct));
    }

    /// <summary>Issue #1025: hatalı testi yeniden aktar.</summary>
    [HttpPost("{id:guid}/tests/{testId:int}/retry")]
    [RequiresEntitlement(EntitlementFeatures.BookImport, InPlanOnly = true)]
    public async Task<IActionResult> RetryTest(Guid id, int testId, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return RetryResult(await _service.RetryTestsAsync(owner, id, testId, ct));
    }

    /// <summary>Issue #1025: bütün hatalı testleri yeniden aktar.</summary>
    [HttpPost("{id:guid}/tests/retry")]
    [RequiresEntitlement(EntitlementFeatures.BookImport, InPlanOnly = true)]
    public async Task<IActionResult> RetryFailedTests(Guid id, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return RetryResult(await _service.RetryTestsAsync(owner, id, null, ct));
    }

    private IActionResult RetryResult(RetryBookImportResult result) => result switch
    {
        RetryBookImportResult.Queued => NoContent(),
        RetryBookImportResult.NotFound => NotFound(new { message = _localizer["bookImport.notFound"].Value }),
        _ => Conflict(new { code = "bookImport.notRetryable", message = _localizer["bookImport.notRetryable"].Value }),
    };

    /// <summary>Issue #1024: Admin — maliyet tavanında bekleyen işi sürdür (tavan yükselir, iş yeniden kuyruğa girer).</summary>
    [HttpPost("{id:guid}/continue")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Continue(Guid id, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return await _service.ContinueAsync(owner, id, ct) switch
        {
            ContinueBookImportResult.Continued => NoContent(),
            ContinueBookImportResult.NotFound => NotFound(new { message = _localizer["bookImport.notFound"].Value }),
            _ => Conflict(new { code = "bookImport.notPaused", message = _localizer["bookImport.notPaused"].Value }),
        };
    }

    /// <summary>Issue #1023: incelemedeki testin soruları (numara, sayfa, cevap; cevapsızlar için A–D seçici).</summary>
    [HttpGet("{id:guid}/tests/{testId:int}/questions")]
    [ProducesResponseType(typeof(List<BookImportReviewQuestionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReviewQuestions(Guid id, int testId, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        var questions = await _service.GetReviewQuestionsAsync(owner, id, testId, ct);
        return questions is null ? NotFound(new { message = _localizer["bookImport.notFound"].Value }) : Ok(questions);
    }

    /// <summary>Issue #1023: taslak sorunun doğru şıkkı (A = 0).</summary>
    [HttpPut("{id:guid}/tests/{testId:int}/questions/{questionId:int}/answer")]
    public async Task<IActionResult> SetAnswer(Guid id, int testId, int questionId, [FromBody] SetBookImportAnswerDto request, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return ReviewResult(await _service.SetAnswerAsync(owner, id, testId, questionId, request?.OptionIndex ?? -1, ct));
    }

    /// <summary>Issue #1023: testi onayla; cevapsız soru varsa 409 <c>bookImport.answerless</c>.</summary>
    [HttpPost("{id:guid}/tests/{testId:int}/approve")]
    public async Task<IActionResult> ApproveTest(Guid id, int testId, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return ReviewResult(await _service.ApproveTestAsync(owner, id, testId, ct));
    }

    /// <summary>Issue #1023: onaylanabilen bütün testleri onayla; atlananlar nedeniyle döner.</summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(ApproveAllBookImportResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveAll(Guid id, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        var result = await _service.ApproveAllAsync(owner, id, ct);
        return result is null ? NotFound(new { message = _localizer["bookImport.notFound"].Value }) : Ok(result);
    }

    /// <summary>İncelemedeki onaylanmamış testi baştan aktar (taslakları silinir, sayfa tespitleri önbellekten).</summary>
    [HttpPost("{id:guid}/tests/{testId:int}/reimport")]
    [RequiresEntitlement(EntitlementFeatures.BookImport, InPlanOnly = true)]
    public async Task<IActionResult> ReimportTest(Guid id, int testId, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return RetryResult(await _service.ReimportTestAsync(owner, id, testId, ct));
    }

    /// <summary>Issue #1023: testi sil — yalnız o testin taslak testi ve taslak soruları.</summary>
    [HttpDelete("{id:guid}/tests/{testId:int}")]
    public async Task<IActionResult> RemoveTest(Guid id, int testId, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        return ReviewResult(await _service.RemoveTestAsync(owner, id, testId, ct));
    }

    /// <summary>
    /// Issue #1023 "İşaretlemede düzelt": testin bir sayfasının taslak sorularını işaretleme ekranında düzeltilenlerle değiştirir
    /// (görsel işin sayfa görseli; cevap verilmezse anahtardan).
    /// </summary>
    [HttpPut("{id:guid}/tests/{testId:int}/pages/{filePage:int}/questions")]
    [RequiresEntitlement(EntitlementFeatures.BookImport, InPlanOnly = true)]
    [ProducesResponseType(typeof(BookImportPageFixDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReplacePage(Guid id, int testId, int filePage, [FromBody] ReplaceBookImportPageDto request,
        [FromServices] IBookImportPageFixService fix, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["bookImport.userNotResolved"].Value });
        try
        {
            var result = await fix.ReplacePageAsync(owner, id, testId, filePage, request?.Questions ?? [], ct);
            return result.Error switch
            {
                BookImportPageFixError.None => Ok(result.Page),
                BookImportPageFixError.NotFound => NotFound(new { message = _localizer["bookImport.notFound"].Value }),
                _ => Conflict(new { code = "bookImport.notInReview", message = _localizer["bookImport.notInReview"].Value }),
            };
        }
        catch (LabelingValidationException ex)
        {
            var message = ex.Args is { Length: > 0 } ? _localizer[ex.Message, ex.Args] : _localizer[ex.Message];
            return BadRequest(new { code = ex.Message, message = message.Value });
        }
        catch (Exception ex) when (ex is LabelingBankSaveException or BookImportFailure)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = _localizer["bookImport.fixFailed"].Value });
        }
    }

    private IActionResult ReviewResult(BookImportReviewResult result) => result switch
    {
        BookImportReviewResult.Ok => NoContent(),
        BookImportReviewResult.NotFound => NotFound(new { message = _localizer["bookImport.notFound"].Value }),
        BookImportReviewResult.Answerless => Conflict(new { code = "bookImport.answerless", message = _localizer["bookImport.answerless"].Value }),
        BookImportReviewResult.InvalidOption => BadRequest(new { code = "bookImport.invalidOption", message = _localizer["bookImport.invalidOption"].Value }),
        _ => Conflict(new { code = "bookImport.notInReview", message = _localizer["bookImport.notInReview"].Value }),
    };

    private NotFoundObjectResult UploadNotFound() =>
        NotFound(new { code = "bookImport.uploadNotFound", message = _localizer["bookImport.uploadNotFound"].Value });

    private BadRequestObjectResult InvalidPart() =>
        BadRequest(new { code = "bookImport.invalidPart", message = _localizer["bookImport.invalidPart"].Value });

    private ObjectResult QuotaReached(EntitlementCheck? quota) =>
        EntitlementLimitResponse.Create(EntitlementFeatures.BookImport, quota, _localizer["bookImport.quotaReached"].Value);

    private async Task<BookImportOwner?> ResolveOwnerAsync(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        return IsResolvedUser(user) ? new BookImportOwner(EntitlementSubject.FromPrincipal(user.Id, User)) : null;
    }

    private static string Camel(StartBookImportError error)
    {
        var name = error.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

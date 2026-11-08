using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1052 (epic #1048): hızlı soru ekleme — fotoğraf yükle, işleri listele, bir işi takip et. Her onaylı öğretmen (ve
/// Admin / QuestionTrainer). İşler kitap aktarımı işleridir (<see cref="BookImportJobKind.QuickAdd"/>); bu uçlar yalnız hızlı
/// eklemeleri, kitap aktarımı uçları yalnız kitapları görür. Kullanıcı kendi işlerini görür, Admin hepsini; başkasının işi 404.
/// </summary>
[ApiController]
[Route("api/quick-add")]
[Authorize(Roles = "Teacher,Admin,QuestionTrainer")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class QuickAddController : BaseController
{
    private const long MaxRequestBytes = QuickAddService.MaxPhotos * QuickAddService.MaxPhotoBytes + 1_000_000;

    private readonly IQuickAddService _quickAdd;
    private readonly IBookImportService _jobs;
    private readonly IStringLocalizer<Messages> _localizer;

    public QuickAddController(IQuickAddService quickAdd, IBookImportService jobs, IStringLocalizer<Messages>? localizer = null)
    {
        _quickAdd = quickAdd;
        _jobs = jobs;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>
    /// Fotoğrafları yükle (en çok <see cref="QuickAddService.MaxPhotos"/>); <c>answers[i]</c> i. fotoğrafın isteğe bağlı cevap
    /// anahtarı. 400: <c>photo</c> hatalı fotoğrafın sırası; 429: aylık hak kalan fotoğraflara yetmiyor.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Start([FromForm] StartQuickAddFormDto request, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["quickAdd.userNotResolved"].Value });

        var files = request?.Photos ?? [];
        var answers = request?.Answers ?? [];
        var streams = files.Select(f => f.OpenReadStream()).ToList();
        try
        {
            var photos = files.Select((f, i) => new QuickAddPhoto(f.FileName, f.Length, streams[i], i < answers.Count ? answers[i] : null))
                .ToList();
            var result = await _quickAdd.StartAsync(owner, new StartQuickAddRequest(request?.GradeId ?? 0, request?.SubjectId, photos), ct);
            return result.Error switch
            {
                StartQuickAddError.None => Ok(result.Job),
                StartQuickAddError.QuotaReached => EntitlementLimitResponse.Create(
                    EntitlementFeatures.QuickAdd, result.Quota, _localizer["quickAdd.quotaReached"].Value),
                var error => BadRequest(new
                {
                    code = $"quickAdd.{Camel(error)}", photo = result.Photo, message = _localizer[$"quickAdd.{Camel(error)}"].Value,
                }),
            };
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<BookImportJobDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int take = 50, CancellationToken ct = default)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["quickAdd.userNotResolved"].Value });
        return Ok(await _jobs.ListAsync(owner, take, ct));
    }

    /// <summary>Bu ayki hak (fotoğraf sayısı); <c>limit</c> null → sınırsız.</summary>
    [HttpGet("quota")]
    public async Task<IActionResult> Quota(CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["quickAdd.userNotResolved"].Value });
        return Ok(await _quickAdd.GetQuotaAsync(owner, ct));
    }

    /// <summary>İşin durumu ve fotoğraf başına satırlar (bulunan / kaydedilen soru, işaretler).</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["quickAdd.userNotResolved"].Value });
        var job = await _jobs.GetAsync(owner, id, ct);
        return job is null ? NotFound(new { message = _localizer["quickAdd.notFound"].Value }) : Ok(job);
    }

    /// <summary>Yüklenen (yönü düzeltilmiş, küçültülmüş) fotoğraf; JPEG.</summary>
    [HttpGet("{id:guid}/photos/{photo:int}")]
    public async Task<IActionResult> GetPhoto(Guid id, int photo, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["quickAdd.userNotResolved"].Value });
        var image = await _jobs.GetPageImageAsync(owner, id, photo, ct);
        return image is null ? NotFound(new { message = _localizer["quickAdd.notFound"].Value }) : File(image, "image/jpeg");
    }

    /// <summary>Hatalı ya da takılmış işi kaldığı yerden yeniden dene (hak tekrar düşmez).</summary>
    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["quickAdd.userNotResolved"].Value });
        return await _jobs.RetryAsync(owner, id, ct) switch
        {
            RetryBookImportResult.Queued => NoContent(),
            RetryBookImportResult.NotFound => NotFound(new { message = _localizer["quickAdd.notFound"].Value }),
            _ => Conflict(new { code = "quickAdd.notRetryable", message = _localizer["quickAdd.notRetryable"].Value }),
        };
    }

    /// <summary>İşi iptal et; <paramref name="deleteDrafts"/>: onaylanmamış taslak sorular da silinir.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromQuery] bool deleteDrafts = false, CancellationToken ct = default)
    {
        var owner = await ResolveOwnerAsync(ct);
        if (owner is null)
            return UserNotResolved(new { message = _localizer["quickAdd.userNotResolved"].Value });
        return await _jobs.CancelAsync(owner, id, deleteDrafts, ct) switch
        {
            CancelBookImportResult.Cancelled => NoContent(),
            CancelBookImportResult.NotFound => NotFound(new { message = _localizer["quickAdd.notFound"].Value }),
            _ => Conflict(new { code = "quickAdd.notCancellable", message = _localizer["quickAdd.notCancellable"].Value }),
        };
    }

    private async Task<BookImportOwner?> ResolveOwnerAsync(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        return IsResolvedUser(user)
            ? new BookImportOwner(EntitlementSubject.FromPrincipal(user.Id, User), BookImportJobKind.QuickAdd)
            : null;
    }

    private static string Camel(StartQuickAddError error)
    {
        var name = error.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

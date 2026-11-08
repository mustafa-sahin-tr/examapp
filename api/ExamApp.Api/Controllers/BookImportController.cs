using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
/// Issue #1018 (epic #1017): kitaptan aktarım — PDF yükle, işleri listele, bir işi takip et. Pilot: yalnız Admin ve
/// QuestionTrainer (işaretleme ve kitap kaydıyla aynı roller ve onaylı öğretmen policy'si). Kullanıcı yalnız kendi işlerini
/// görür, Admin hepsini; başkasının işi 404.
/// </summary>
[ApiController]
[Route("api/book-imports")]
[Authorize(Roles = "Admin,QuestionTrainer")]
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
            StartBookImportError.QuotaReached => StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                code = "entitlement.limitReached",
                feature = EntitlementFeatures.BookImport,
                limit = result.Quota?.Limit,
                used = result.Quota?.Used,
                resetsAtUtc = result.Quota?.ResetsAtUtc,
                message = _localizer["bookImport.quotaReached"].Value,
            }),
            var error => BadRequest(new { code = $"bookImport.{Camel(error)}", message = _localizer[$"bookImport.{Camel(error)}"].Value }),
        };
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

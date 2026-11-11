using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.AskTeacher;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #1111: soru gönderme formu (multipart).</summary>
public class AskTeacherSendFormDto
{
    public IFormFile? Photo { get; set; }
    public int TeacherId { get; set; }
    public int? SubjectId { get; set; }
    public string? Note { get; set; }
}

/// <summary>Issue #1111: öğretmenin cevabı (multipart): yazı ve / veya görsel.</summary>
public class AskTeacherAnswerFormDto
{
    public string? Text { get; set; }
    public IFormFile? Image { get; set; }
}

public class AskTeacherRatingDto
{
    public int Rating { get; set; }
}

/// <summary>
/// Issue #1111: öğretmene soru sor. Öğrenci uçları kendi sorularını, öğretmen uçları (<c>queue</c>) kendisine gelenleri görür;
/// başkasının sorusu 404. Görseller yalnız bu uçlardan (sorunun öğrencisi ve öğretmeni) okunur. Gateway: <c>/api/exam/ask-teacher</c>.
/// </summary>
[ApiController]
[Route("api/ask-teacher")]
public class AskTeacherController : BaseController
{
    private const long MaxRequestBytes = AskTeacherService.MaxImageBytes + 100_000;

    private readonly IAskTeacherService _service;
    private readonly IStringLocalizer<Messages> _localizer;

    public AskTeacherController(IAskTeacherService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    // ---- Öğrenci ----------------------------------------------------------------------------------------------

    /// <summary>Soru gönderilebilecek öğretmenler; ders verildiyse o dersi verenler önce.</summary>
    [HttpGet("teachers")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(List<AskTeacherTeacherDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Teachers([FromQuery] int? subjectId, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        var teachers = await _service.GetTeachersAsync(userId.Value, subjectId, ct);
        return teachers is null ? Error(AskTeacherError.StudentNotFound) : Ok(teachers);
    }

    [HttpGet("quota")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(AskTeacherQuotaDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Quota(CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        return Ok(await _service.GetQuotaAsync(EntitlementSubject.FromPrincipal(userId.Value, User), ct));
    }

    /// <summary>Fotoğraf + isteğe bağlı not. 403 planda yok, 429 hak (bekleyenler dahil) dolu, 404 öğretmen uygun değil.</summary>
    [HttpPost]
    [Authorize(Roles = "Student")]
    [Authorize(Policy = ExamApp.Api.Services.Parents.Enforcement.ParentRequirementPolicies.ParentActivated)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Send([FromForm] AskTeacherSendFormDto form, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        if (form.Photo is null)
            return Error(AskTeacherError.InvalidImage);
        await using var stream = form.Photo.OpenReadStream();
        var result = await _service.SendAsync(EntitlementSubject.FromPrincipal(userId.Value, User),
            new AskTeacherSendRequest(form.TeacherId, form.SubjectId, form.Note, new AskTeacherImage(form.Photo.Length, stream)), ct);
        return result.Error switch
        {
            AskTeacherError.None => StatusCode(StatusCodes.Status201Created, result.Question),
            AskTeacherError.NotInPlan => EntitlementLimitResponse.NotInPlan(EntitlementFeatures.AskTeacher, result.Quota),
            AskTeacherError.QuotaReached => EntitlementLimitResponse.Create(EntitlementFeatures.AskTeacher, result.Quota,
                _localizer["askTeacher.quotaReached"].Value),
            var error => Error(error),
        };
    }

    [HttpGet]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(List<AskTeacherQuestionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        return userId is null ? NotResolved() : Ok(await _service.ListForStudentAsync(userId.Value, ct));
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(AskTeacherQuestionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        var question = await _service.GetForStudentAsync(userId.Value, id, ct);
        return question is null ? Error(AskTeacherError.NotFound) : Ok(question);
    }

    /// <summary>Cevaplanmamış soruyu geri çek (hak düşmez).</summary>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        var error = await _service.CancelAsync(userId.Value, id, ct);
        return error == AskTeacherError.None ? NoContent() : Error(error);
    }

    [HttpPut("{id:int}/rating")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Rate(int id, [FromBody] AskTeacherRatingDto body, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        var error = await _service.RateAsync(userId.Value, id, body?.Rating ?? 0, ct);
        return error == AskTeacherError.None ? NoContent() : Error(error);
    }

    // ---- Ortak: görseller ----------------------------------------------------------------------------------------

    /// <summary>Soru görseli (JPEG); yalnız sorunun öğrencisi ve öğretmeni.</summary>
    [HttpGet("{id:int}/image")]
    [Authorize(Roles = "Student,Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    public Task<IActionResult> Image(int id, CancellationToken ct) => ImageAsync(id, answer: false, ct);

    [HttpGet("{id:int}/answer-image")]
    [Authorize(Roles = "Student,Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    public Task<IActionResult> AnswerImage(int id, CancellationToken ct) => ImageAsync(id, answer: true, ct);

    private async Task<IActionResult> ImageAsync(int id, bool answer, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        var image = await _service.GetImageAsync(userId.Value, id, answer, ct);
        if (image is null)
            return Error(AskTeacherError.NotFound);
        Response.Headers.CacheControl = "private, max-age=300";
        return File(image, "image/jpeg");
    }

    // ---- Öğretmen ----------------------------------------------------------------------------------------------

    /// <summary>Gelen sorular; <c>status</c> verilmezse hepsi (açıklar süresi yakın olandan).</summary>
    [HttpGet("queue")]
    [Authorize(Roles = "Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    [ProducesResponseType(typeof(List<AskTeacherQuestionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Queue([FromQuery] AskTeacherStatus? status, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        return userId is null ? NotResolved() : Ok(await _service.QueueAsync(userId.Value, status, ct));
    }

    [HttpGet("queue/{id:int}")]
    [Authorize(Roles = "Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    [ProducesResponseType(typeof(AskTeacherQuestionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> QueueItem(int id, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        var question = await _service.GetForTeacherAsync(userId.Value, id, ct);
        return question is null ? Error(AskTeacherError.NotFound) : Ok(question);
    }

    /// <summary>Cevap: yazı ve / veya görsel. Öğrencinin hakkından bir kullanım düşer.</summary>
    [HttpPost("queue/{id:int}/answer")]
    [Authorize(Roles = "Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Answer(int id, [FromForm] AskTeacherAnswerFormDto form, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        await using var stream = form.Image?.OpenReadStream();
        var image = form.Image is null ? null : new AskTeacherImage(form.Image.Length, stream!);
        var result = await _service.AnswerAsync(userId.Value, id, form.Text, image, ct);
        return result.Error == AskTeacherError.None ? Ok(result.Question) : Error(result.Error);
    }

    private async Task<int?> UserIdAsync(CancellationToken ct)
    {
        if (IsServiceAccount)
            return null;
        var user = await GetAuthenticatedUserAsync(ct);
        return IsResolvedUser(user) ? user.Id : null;
    }

    private IActionResult NotResolved() =>
        IsServiceAccount ? Forbid() : UserNotResolved(new { message = _localizer["askTeacher.userNotResolved"].Value });

    private IActionResult Error(AskTeacherError error)
    {
        var code = char.ToLowerInvariant(error.ToString()[0]) + error.ToString()[1..];
        var body = new { code = $"askTeacher.{code}", message = _localizer[$"askTeacher.{code}"].Value };
        return error switch
        {
            AskTeacherError.NotFound or AskTeacherError.TeacherNotAvailable or AskTeacherError.StudentNotFound => NotFound(body),
            AskTeacherError.InvalidState => Conflict(body),
            _ => BadRequest(body),
        };
    }
}

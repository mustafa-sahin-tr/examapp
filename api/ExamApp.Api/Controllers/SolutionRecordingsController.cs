using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.SolutionRecordings;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1226: soru çözüm kaydı (kalem çizgileri + ses). Öğretmen uçları (<c>question/{id}</c>) yalnız soru sahibine ve
/// admine açıktır; başkasının sorusu 404. Öğrenci uçları (<c>session-question/{id}</c>) test oturumu satırıyla çalışır ve
/// kaydı yalnız bitmiş oturumda ya da örnek soruda verir, öncesinde 404. Ses yalnız bu uçlardan okunur (MinIO nesnesi özel).
/// Gateway: <c>/api/exam/solution-recordings</c>.
/// </summary>
[ApiController]
[Route("api/solution-recordings")]
public class SolutionRecordingsController : BaseController
{
    private const string AuthoringRoles = "Teacher,Admin";
    private const long MaxRequestBytes = QuestionSolutionRecordingService.MaxAudioBytes + SolutionStrokeDocument.MaxJsonLength + 100_000;

    private readonly IQuestionSolutionRecordingService _service;
    private readonly IQuestionOwnershipGuard _ownership;
    private readonly IStringLocalizer<Messages> _localizer;

    public SolutionRecordingsController(IQuestionSolutionRecordingService service, IQuestionOwnershipGuard ownership,
        IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _ownership = ownership;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    // ---- Öğretmen ----------------------------------------------------------------------------------------------

    [HttpGet("question/{questionId:int}")]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    [ProducesResponseType(typeof(QuestionSolutionRecordingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int questionId, CancellationToken ct)
    {
        if (!await OwnsAsync(questionId, ct))
            return NotFoundError();
        var recording = await _service.GetAsync(questionId, ct);
        return recording is null ? NotFoundError() : Ok(recording);
    }

    [HttpGet("question/{questionId:int}/audio")]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> Audio(int questionId, CancellationToken ct) =>
        await OwnsAsync(questionId, ct) ? await AudioAsync(questionId, ct) : NotFoundError();

    /// <summary>Kaydı oluşturur ya da değiştirir. Ses en çok 5 dakika; çizgiler JSON metni olarak aynı formda.</summary>
    [HttpPut("question/{questionId:int}")]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes, ValueLengthLimit = SolutionStrokeDocument.MaxJsonLength)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(QuestionSolutionRecordingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(int questionId, [FromForm] QuestionSolutionRecordingFormDto form, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["solutionRecording.userNotResolved"].Value });
        if (await _ownership.CanModifyQuestionAsync(questionId, user.Id, IsAdmin, ct) != QuestionAccessResult.Allowed)
            return NotFoundError();
        if (form.Audio is null)
            return Error(SolutionRecordingError.InvalidAudio);
        await using var audio = form.Audio.OpenReadStream();
        var result = await _service.SaveAsync(questionId, user.Id, form.Strokes, form.DurationMs, audio, form.Audio.Length,
            form.Audio.ContentType, ct);
        return result.Error == SolutionRecordingError.None ? Ok(result.Recording) : Error(result.Error);
    }

    [HttpDelete("question/{questionId:int}")]
    [Authorize(Roles = AuthoringRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> Delete(int questionId, CancellationToken ct)
    {
        if (!await OwnsAsync(questionId, ct))
            return NotFoundError();
        return await _service.DeleteAsync(questionId, ct) ? NoContent() : NotFoundError();
    }

    // ---- Öğrenci -----------------------------------------------------------------------------------------------

    /// <summary>Test oturumu satırının (<c>TestInstanceQuestion.Id</c>) sorusuna ait kayıt; oturum bitmeden 404.</summary>
    [HttpGet("session-question/{instanceQuestionId:int}")]
    [Authorize(Roles = "Student")]
    [ProducesResponseType(typeof(QuestionSolutionRecordingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForStudent(int instanceQuestionId, CancellationToken ct)
    {
        var questionId = await StudentQuestionAsync(instanceQuestionId, ct);
        var recording = questionId is { } id ? await _service.GetAsync(id, ct) : null;
        return recording is null ? NotFoundError() : Ok(recording);
    }

    [HttpGet("session-question/{instanceQuestionId:int}/audio")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> AudioForStudent(int instanceQuestionId, CancellationToken ct) =>
        await StudentQuestionAsync(instanceQuestionId, ct) is { } questionId ? await AudioAsync(questionId, ct) : NotFoundError();

    // ---- Ortak -------------------------------------------------------------------------------------------------

    private async Task<bool> OwnsAsync(int questionId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        return IsResolvedUser(user)
            && await _ownership.CanModifyQuestionAsync(questionId, user.Id, IsAdmin, ct) == QuestionAccessResult.Allowed;
    }

    private async Task<int?> StudentQuestionAsync(int instanceQuestionId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        return IsResolvedUser(user) ? await _service.ResolveForStudentAsync(user.Id, instanceQuestionId, ct) : null;
    }

    private async Task<IActionResult> AudioAsync(int questionId, CancellationToken ct)
    {
        var audio = await _service.GetAudioAsync(questionId, ct);
        if (audio is null)
            return NotFoundError();
        Response.Headers.CacheControl = "private, max-age=300";
        // Aralık istekleri: oynatıcı ileri / geri sarabilsin.
        return File(audio.Content, audio.ContentType, enableRangeProcessing: true);
    }

    private IActionResult NotFoundError() =>
        NotFound(new { code = "solutionRecording.notFound", message = _localizer["solutionRecording.notFound"].Value });

    private IActionResult Error(SolutionRecordingError error)
    {
        var code = char.ToLowerInvariant(error.ToString()[0]) + error.ToString()[1..];
        var body = new { code = $"solutionRecording.{code}", message = _localizer[$"solutionRecording.{code}"].Value };
        return error == SolutionRecordingError.StorageFailed
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, body)
            : BadRequest(body);
    }
}

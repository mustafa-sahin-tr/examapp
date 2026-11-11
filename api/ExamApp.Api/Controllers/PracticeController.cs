using ExamApp.Api.Data;
using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Practice;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// "Soru Çöz" pratik oturumu (issue #62). Gateway'de mevcut <c>/api/exam/{everything}</c> joker route'u
/// bu controller'ı da kapsar; ayrı Ocelot kaydı gerekmez.
/// </summary>
[Route("api/practice")]
[ApiController]
[Authorize(Roles = "Student")]
public class PracticeController : BaseController
{
    private readonly IPracticeSessionService _practice;
    private readonly IDailyQuestionSetService _daily;
    private readonly IStudentService _studentService;

    // Client'a dönen tüm metinler mesaj sözlüğünden gelir (issue #184).
    // DI her zaman gerçek localizer'ı verir; parametre yalnızca DI'siz kurulan (birim test)
    // senaryolarda varsayılan dile düşebilmek için opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    public PracticeController(
        IPracticeSessionService practice,
        IDailyQuestionSetService daily,
        IStudentService studentService,
        IStringLocalizer<Messages>? localizer = null)
        : base()
    {
        _practice = practice;
        _daily = daily;
        _studentService = studentService;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>Yeni pratik oturumu açar. Gövde boş/eksikse: öğrencinin sınıfı + tüm dersler.</summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> StartSession([FromBody] PracticeSessionStartDto? request, [FromServices] IEntitlementService entitlements,
        CancellationToken ct)
    {
        var (student, error, userId) = await ResolveStudentWithUserAsync();
        if (error != null)
            return error;

        request ??= new PracticeSessionStartDto();
        var (feature, counted) = PlanFeatureOf(request);
        var check = await entitlements.CheckAsync(EntitlementSubject.FromPrincipal(userId, User), feature, ct);
        if (check.Limit == 0)
            return EntitlementLimitResponse.NotInPlan(feature, check);
        if (counted && !check.Allowed)
            return EntitlementLimitResponse.Create(feature, check);

        var scope = await StudyScope.ForAsync(entitlements, userId, ct);
        try
        {
            var result = await _practice.StartAsync(student!, request, ct, scope.SubjectId);
            if (counted)
                await entitlements.RecordUsageAsync(userId, feature, $"{feature}:session:{result.Id}", ct);
            return CreatedAtAction(nameof(GetSession), new { id = result.Id }, result);
        }
        catch (StudyScopeException ex)
        {
            return EntitlementLimitResponse.OutOfScope(ex.ScopeSubjectId, scope.PlanKey);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Issue #529: tekrar zamanı gelen sorular. Hesaplanır; sınıfı olmayan öğrencide boş döner.</summary>
    [HttpGet("due-review")]
    [RequiresEntitlement(EntitlementFeatures.StudyTools, InPlanOnly = true)]
    public async Task<IActionResult> GetDueReview([FromServices] IEntitlementService entitlements, [FromQuery] int? subjectId,
        [FromQuery] int take = 15, CancellationToken ct = default)
    {
        var (student, error, userId) = await ResolveStudentWithUserAsync();
        if (error != null)
            return error;

        // #1109: tek ders planında liste o derse daralır; başka ders istenirse 403.
        var scope = await StudyScope.ForAsync(entitlements, userId, ct);
        if (!scope.Covers(subjectId))
            return EntitlementLimitResponse.OutOfScope(scope.SubjectId!.Value, scope.PlanKey);
        return Ok(await _practice.GetDueReviewAsync(student!, subjectId ?? scope.SubjectId, take, ct));
    }

    /// <summary>Issue #532: kaydedilen sorular.</summary>
    [HttpGet("saved")]
    [RequiresEntitlement(EntitlementFeatures.StudyTools, InPlanOnly = true)]
    public async Task<IActionResult> GetSavedQuestions(CancellationToken ct = default)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        return Ok(await _practice.GetSavedQuestionsAsync(student!, ct));
    }

    /// <summary>Issue #532: soruyu kaydeder (idempotent). Görülmemiş ya da bitmemiş testteki soru 403, sınır dolduysa 409.</summary>
    [HttpPut("saved/{questionId:int}")]
    [RequiresEntitlement(EntitlementFeatures.StudyTools, InPlanOnly = true)]
    public async Task<IActionResult> SaveQuestion(int questionId, CancellationToken ct = default)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        return await _practice.SaveQuestionAsync(student!, questionId, ct) switch
        {
            SaveQuestionResult.Saved => NoContent(),
            SaveQuestionResult.LimitReached => Conflict(new { message = _localizer["practice.savedLimitReached"].Value }),
            _ => StatusCode(StatusCodes.Status403Forbidden),
        };
    }

    /// <summary>Issue #532: kaydı kaldırır.</summary>
    [HttpDelete("saved/{questionId:int}")]
    public async Task<IActionResult> UnsaveQuestion(int questionId, CancellationToken ct = default)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        await _practice.UnsaveQuestionAsync(student!.Id, questionId, ct);
        return NoContent();
    }

    /// <summary>Issue #698: yanlış defteri; sınıfı olmayan öğrencide boş döner.</summary>
    [HttpGet("mistakes")]
    [RequiresEntitlement(EntitlementFeatures.StudyTools, InPlanOnly = true)]
    public async Task<IActionResult> GetMistakes([FromServices] IEntitlementService entitlements, [FromQuery] int? subjectId,
        [FromQuery] int? topicId, CancellationToken ct = default)
    {
        var (student, error, userId) = await ResolveStudentWithUserAsync();
        if (error != null)
            return error;

        // #1109: tek ders planında defter o derse daralır (başka dersin konusu boş döner); başka ders istenirse 403.
        var scope = await StudyScope.ForAsync(entitlements, userId, ct);
        if (!scope.Covers(subjectId))
            return EntitlementLimitResponse.OutOfScope(scope.SubjectId!.Value, scope.PlanKey);
        return Ok(await _practice.GetMistakesAsync(student!, subjectId ?? scope.SubjectId, topicId, ct));
    }

    /// <summary>Issue #698: yanlış nedeni etiketi; soru öğrencinin defterinde değilse 404.</summary>
    [HttpPut("mistakes/{questionId:int}/reason")]
    [RequiresEntitlement(EntitlementFeatures.StudyTools, InPlanOnly = true)]
    public async Task<IActionResult> SetMistakeReason(int questionId, [FromBody] MistakeReasonUpdateDto request, CancellationToken ct = default)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        MistakeReason? reason = null;
        if (request.Reason != null)
        {
            if (!Enum.TryParse<MistakeReason>(request.Reason, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
                return BadRequest();
            reason = parsed;
        }

        return await _practice.SetMistakeReasonAsync(student!, questionId, reason, ct) ? NoContent() : NotFound();
    }

    /// <summary>
    /// "Günün soruları" (issue #99): bugünün (Europe/Istanbul) seti ve ilerlemesi; set yoksa ilk istekte üretilir.
    /// Havuz boşsa ya da sınıf tanımlı değilse 200 + <c>status: "Empty"</c> (hata değil).
    /// </summary>
    [HttpGet("daily")]
    [EnableRateLimiting(DailyQuestionsRateLimiting.Policy)]
    public async Task<IActionResult> GetDaily(CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        return Ok(await _daily.GetTodayAsync(student!, ct));
    }

    /// <summary>
    /// Bugünün setinin pratik oturumunu açar ya da mevcut oturumu döner (idempotent). Tamamlanmış sette yeni oturum
    /// açılmaz: 200 + mevcut <c>sessionId</c> + <c>status: "Completed"</c>. Set boşsa 409.
    /// #66: gövde isteğe bağlı ders/konu/alt konu taşır; oturum açılmamış sette set bu kapsamdan üretilir, açılmış sette
    /// yok sayılır. Geçersiz kapsam 400.
    /// </summary>
    [HttpPost("daily/start")]
    [EnableRateLimiting(DailyQuestionsRateLimiting.Policy)]
    public async Task<IActionResult> StartDaily([FromBody] DailyStartRequestDto? request, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        DailyStartResultDto? result;
        try
        {
            result = await _daily.StartTodayAsync(student!, request, ct);
        }
        catch (DailyScopeInvalidException)
        {
            return BadRequest(new { message = _localizer["practice.dailyScopeInvalid"].Value });
        }
        catch (DailySetAlreadyStartedException)
        {
            return Conflict(new { message = _localizer["practice.dailyAlreadyStarted"].Value, errorCode = "DailySetAlreadyStarted" });
        }

        if (result == null)
            return Conflict(new { message = _localizer["practice.dailyEmpty"].Value });

        return Ok(result);
    }

    /// <summary>Öğrencinin geçmiş pratik oturumları, en yeni önce. Sayfalı (<see cref="Paged{T}"/>).</summary>
    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        var result = await _practice.ListAsync(student!.Id, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Oturumdaki soruların tek tek sonucu (doğru şık dahil); geçmiş oturum incelemesi.</summary>
    [HttpGet("sessions/{id:int}/review")]
    public async Task<IActionResult> GetSessionReview(int id, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        var result = await _practice.GetReviewAsync(id, student!.Id, ct);
        if (result == null)
            return NotFound(new { message = _localizer["practice.sessionNotFound"].Value });

        return Ok(result);
    }

    [HttpGet("sessions/{id:int}")]
    public async Task<IActionResult> GetSession(int id, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        var result = await _practice.GetAsync(id, student!.Id, ct);
        if (result == null)
            return NotFound(new { message = _localizer["practice.sessionNotFound"].Value });

        return Ok(result);
    }

    /// <summary>
    /// Oturumda henüz gösterilmemiş rastgele bir soru. Havuz bittiğinde 200 +
    /// <c>{ question: null, poolExhausted: true }</c> döner (404 değil).
    /// </summary>
    [HttpGet("sessions/{id:int}/next")]
    public async Task<IActionResult> NextQuestion(int id, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        try
        {
            var result = await _practice.NextQuestionAsync(id, student!.Id, ct);
            if (result == null)
                return NotFound(new { message = _localizer["practice.sessionNotFound"].Value });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("sessions/{id:int}/answer")]
    public async Task<IActionResult> SubmitAnswer(int id, [FromBody] PracticeAnswerSubmitDto dto, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        try
        {
            // Keycloak sub: günlük set cevabının AnswerSubmittedEvent.ClientId'si (issue #99, rozet bildirimi hedefi).
            var result = await _practice.SubmitAnswerAsync(id, student!.Id, dto, KeyCloakId, ct);
            if (result == null)
                return NotFound(new { message = _localizer["practice.sessionNotFound"].Value });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("sessions/{id:int}/end")]
    public async Task<IActionResult> EndSession(int id, CancellationToken ct)
    {
        var (student, error) = await ResolveStudentAsync();
        if (error != null)
            return error;

        var result = await _practice.EndAsync(id, student!.Id, ct);
        if (result == null)
            return NotFound(new { message = _localizer["practice.sessionNotFound"].Value });

        return Ok(result);
    }

    // --- helpers ---

    private async Task<(StudentProfileDto? Student, IActionResult? Error)> ResolveStudentAsync()
    {
        var (student, error, _) = await ResolveStudentWithUserAsync();
        return (student, error);
    }

    private async Task<(StudentProfileDto? Student, IActionResult? Error, int UserId)> ResolveStudentWithUserAsync()
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return (null, UserNotResolved(_localizer["auth.authenticationFailed"].Value), 0);

        var student = await _studentService.GetStudentProfile(user.Id);
        if (student == null)
            return (null, NotFound(_localizer["student.profileNotFound"].Value), 0);

        return (student, null, user.Id);
    }

    /// <summary>
    /// Issue #1089: oturumun plan özelliği. Karışık mini deneme <c>mixed-mini</c>, konu seçip çözme <c>practice</c> (açılışta
    /// sayılır); tekrar, yanlış defteri, kaydedilenler ve çalışma sayfası <c>study-tools</c> (yalnız planda olması).
    /// </summary>
    internal static (string Feature, bool Counted) PlanFeatureOf(PracticeSessionStartDto request) =>
        request.MixedReview ? (EntitlementFeatures.MixedMini, true)
        : request.DueReview || request.Mistakes || request.Saved || request.StudyItemId != null ? (EntitlementFeatures.StudyTools, false)
        : (EntitlementFeatures.Practice, true);
}

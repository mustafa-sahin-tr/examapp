using ExamApp.Api.Services.Teachers.Authorization;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Ders planlama / randevu uçları (issue #96). Gateway üzerinden <c>/api/exam/booking/...</c> ile erişilir.
/// Sahiplik kontrolü servis katmanındadır; burada yalnızca rol + HTTP eşlemesi yapılır.
/// </summary>
[Route("api/booking")]
[ApiController]
public class BookingController : BaseController
{
    private readonly IBookingService _bookingService;
    private readonly IRecurringAvailabilityService _recurringAvailability;

    // Client'a donen tum metinler mesaj sozlugunden gelir (issue #184).
    private readonly IStringLocalizer<Messages> _localizer;

    private readonly IAuthorizationService _authorization;

    public BookingController(
        IBookingService bookingService,
        IRecurringAvailabilityService recurringAvailability,
        IStringLocalizer<Messages> localizer,
        IAuthorizationService authorization) : base()
    {
        _authorization = authorization;
        _bookingService = bookingService;
        _recurringAvailability = recurringAvailability;
        _localizer = localizer;
    }

    // ---------------- Müsaitlik slotları (öğretmen) ----------------

    /// <summary>Öğretmen kendi adına bir müsaitlik aralığı tanımlar.</summary>
    [HttpPost("slots")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> CreateSlot([FromBody] CreateAvailabilitySlotDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.CreateSlotAsync(user.Id, request, ct);
        if (!result.Success)
            return MapFailure(result);

        return CreatedAtAction(nameof(GetMySlots), new { }, result);
    }

    /// <summary>Öğretmenin kendi müsaitlik aralıkları (geçmiş + gelecek), aktif randevu durumuyla.</summary>
    [HttpGet("slots/mine")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> GetMySlots([FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.GetMySlotsAsync(user.Id, skip, take, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>
    /// Öğretmen kendi, aktif randevusu olmayan müsaitlik aralığını siler (soft delete). Yanıtlar:
    /// 204 silindi; 404 slot (ya da öğretmen kaydı) yok / zaten silinmiş; 403 slot başka öğretmenin;
    /// 409 aktif (Pending/Approved) randevu var → <c>errorCode: "SlotHasActiveBooking"</c> (issue #376) ya da öğretmen
    /// müsaitlik kilidi zaman aşımı (<c>booking.slot.busy</c>, errorCode yok — tekrar denenebilir).
    /// </summary>
    [HttpDelete("slots/{id:int}")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteSlot(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.DeleteSlotAsync(user.Id, id, ct);
        return result.Success ? NoContent() : MapFailure(result);
    }

    // ---------------- Tekrarlayan haftalık kurallar (issue #178) ----------------

    /// <summary>
    /// Öğretmen "her hafta tekrarla" kuralı tanımlar; 90 günlük ufka kadar somut slotlar üretilir.
    /// Çakışan haftalar atlanır ve yanıtta <c>skippedDates</c> olarak döner.
    /// </summary>
    [HttpPost("recurring-rules")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> CreateRecurringRule(
        [FromBody] CreateRecurringAvailabilityRuleDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _recurringAvailability.CreateRuleAsync(user.Id, request, ct);
        if (!result.Success)
            return MapFailure(result);

        return CreatedAtAction(nameof(GetMyRecurringRules), new { }, result);
    }

    /// <summary>Öğretmenin aktif tekrarlayan kuralları.</summary>
    [HttpGet("recurring-rules/mine")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> GetMyRecurringRules([FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _recurringAvailability.GetMyRulesAsync(user.Id, skip, take, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>
    /// "Tüm seri": kuralı kapatır ve bugünden itibaren aktif randevusu olmayan üretilmiş slotları siler.
    /// Randevulu slotlar korunur; yanıt hangilerinin korunduğunu döner. "Sadece bu hafta" için
    /// mevcut <c>DELETE slots/{id}</c> kullanılır.
    /// </summary>
    [HttpDelete("recurring-rules/{id:int}")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteRecurringRule(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _recurringAvailability.DeleteRuleAsync(user.Id, id, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    // ---------------- Müsaitlik slotları (öğrenci görünümü) ----------------

    /// <summary>Onaylı bir öğretmenin gelecekteki, henüz randevu alınmamış müsaitlik aralıkları.</summary>
    [HttpGet("teachers/{teacherId:int}/slots")]
    [Authorize]
    public async Task<IActionResult> GetTeacherSlots(int teacherId, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
    {
        // issue #418: yalnız bağımsız tutor'un takvimi açık (herkese); okula bağlı öğretmen → 404 (#190 aynı-okul istisnası kalktı).
        var result = await _bookingService.GetTeacherOpenSlotsAsync(teacherId, skip, take, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    // ---------------- Randevu talepleri ----------------

    /// <summary>
    /// Öğrenci bir müsaitlik aralığı için randevu talebi oluşturur. issue #439: veliye tabi öğrencinin talebi
    /// <c>AwaitingParent</c> açılır (öğretmene gitmez, velilere bildirim); Active velisi yoksa 403 +
    /// <c>errorCode: "ParentRequired"</c>. Yetişkin rolü olan hesap (Teacher/Admin/Parent ile birlikte Student) eski akışta kalır.
    /// </summary>
    [HttpPost("requests")]
    [Authorize(Roles = "Student")]
    [Authorize(Policy = ExamApp.Api.Services.Parents.Enforcement.ParentRequirementPolicies.ParentActivated)] // issue #440: veli daveti aktive edilmemiş öğrenci 403 ParentActivationRequired
    public async Task<IActionResult> CreateRequest([FromBody] CreateBookingDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.CreateBookingAsync(user.Id, request, ParentRequirement.RequiresParent(User), ct);
        if (!result.Success)
            return MapFailure(result);

        return CreatedAtAction(nameof(GetStudentRequests), new { }, result);
    }

    /// <summary>Öğretmene gelen randevu talepleri (issue #439: veli onayından geçmemiş / veliden dönmüş talepler hariç).</summary>
    [HttpGet("requests/teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> GetTeacherRequests([FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.GetTeacherBookingsAsync(user.Id, skip, take, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>Öğrencinin kendi randevu talepleri.</summary>
    [HttpGet("requests/student")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetStudentRequests([FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.GetStudentBookingsAsync(user.Id, skip, take, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>
    /// Öğretmen kendi slotuna gelen talebi onaylar. Slotu silinmiş talep onaylanamaz → 409 +
    /// <c>errorCode: "BookingRequestSlotDeleted"</c> (issue #376; yalnız reddedilebilir).
    /// </summary>
    [HttpPost("requests/{id:int}/approve")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> ApproveRequest(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.ApproveBookingAsync(user.Id, id, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>Öğretmen kendi slotuna gelen talebi reddeder. Gövde (gerekçe) opsiyoneldir.</summary>
    [HttpPost("requests/{id:int}/reject")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> RejectRequest(int id, [FromBody] RejectBookingDto? request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.RejectBookingAsync(user.Id, id, request?.RejectionReason, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    // ---------------- İptal / erteleme / no-show (issue #315) ----------------

    /// <summary>
    /// Randevuyu iptal eder (öğrenci: Pending/AwaitingParent/Approved; öğretmen: Approved). Onaylı randevu başlangıca
    /// <c>BookingPolicy:CancellationMinNoticeHours</c> kalana kadar iptal edilebilir → aksi halde 409 +
    /// <c>CancellationWindowClosed</c>. Gerekçe opsiyonel, karşı tarafa bildirilir.
    /// </summary>
    [HttpPost("requests/{id:int}/cancel")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [Authorize(Roles = "Teacher,Student")]
    [ExamApp.Api.Services.Parents.Enforcement.AllowWhenParentRequired] // velisi gelmeyen öğrenci de dersini iptal edip slotu bırakabilir
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(ExamApp.Api.Helpers.BookingLifecycleRateLimiting.Policy)]
    public async Task<IActionResult> CancelRequest(int id, [FromBody] CancelBookingDto? request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.CancelBookingAsync(user.Id, id, request?.Reason, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>Onaylı randevu için aynı öğretmenin boş bir slotunu erteleme olarak önerir (karşı taraf onaylar).</summary>
    [HttpPost("requests/{id:int}/reschedule")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [Authorize(Roles = "Teacher,Student")]
    [Authorize(Policy = ExamApp.Api.Services.Parents.Enforcement.ParentRequirementPolicies.ParentActivated)] // #440
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(ExamApp.Api.Helpers.BookingLifecycleRateLimiting.Policy)]
    public async Task<IActionResult> ProposeReschedule(int id, [FromBody] ProposeRescheduleDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.ProposeRescheduleAsync(user.Id, id, request.AvailabilitySlotId, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>Bekleyen erteleme önerisini kabul/ret eder; öneren taraf <c>accept=false</c> ile geri çeker.</summary>
    [HttpPost("requests/{id:int}/reschedule/respond")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [Authorize(Roles = "Teacher,Student")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(ExamApp.Api.Helpers.BookingLifecycleRateLimiting.Policy)]
    public async Task<IActionResult> RespondReschedule(int id, [FromBody] RespondRescheduleDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        // Kabul yeni bir ders saati bağlar → veli daveti aktive edilmemiş öğrenciye kapalı (#440, propose ile aynı). Ret / geri
        // çekme açık kalır: kısıtlı öğrenci kendi önerisini geri çekebilsin (code review U5).
        if (request.Accept)
        {
            var parentGate = await _authorization.AuthorizeAsync(
                User, ExamApp.Api.Services.Parents.Enforcement.ParentRequirementPolicies.ParentActivated);
            if (!parentGate.Succeeded)
            {
                // Bildirimsel policy'lerle aynı 403 gövdesi ({ errorCode: "ParentActivationRequired" }).
                await ExamApp.Api.Services.Parents.Enforcement.ParentRequirementResponses.WriteAsync(
                    HttpContext, ExamApp.Api.Models.Dtos.ParentInvitations.ParentRequirementErrorCodes.ParentActivationRequired);
                return new EmptyResult();
            }
        }

        var result = await _bookingService.RespondRescheduleAsync(user.Id, id, request.Accept, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>Ders bitişinden sonra karşı tarafın derse katılmadığını bildirir.</summary>
    [HttpPost("requests/{id:int}/no-show")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [Authorize(Roles = "Teacher,Student")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(ExamApp.Api.Helpers.BookingLifecycleRateLimiting.Policy)]
    public async Task<IActionResult> ReportNoShow(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.ReportNoShowAsync(user.Id, id, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    // ---------------- Görüşme odası (issue #97) ----------------

    /// <summary>
    /// Onaylı bir randevu için görüşme odası bilgisi + katılım token'ı döner. Hem öğretmen hem
    /// öğrenci aynı ucu çağırır; token'daki moderatör yetkisini servis katmanı belirler.
    /// </summary>
    [HttpPost("requests/{id:int}/video-session")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)] // issue #287
    [Authorize(Roles = "Teacher,Student")]
    public async Task<IActionResult> CreateVideoSession(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.GetVideoSessionAsync(user.Id, id, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>
    /// issue #320: açık görüşme sayfasının periyodik yoklaması — token üretmez. <c>active=false</c> (askı, iptal, pencere
    /// kapandı) gelince istemci Jitsi'yi kapatır. Askıdaki öğretmenin kendisi ApprovedTeacher policy'sinde 403
    /// <c>TeacherNotApproved</c> alır; istemci bunu da sonlanma sayar.
    /// </summary>
    [HttpGet("requests/{id:int}/live-status")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [Authorize(Roles = "Teacher,Student")]
    public async Task<IActionResult> GetLiveSessionStatus(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return UserNotResolved(_localizer["booking.unauthorized"].Value);

        var result = await _bookingService.GetLiveSessionStatusAsync(user.Id, id, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>ResponseBaseDto bayraklarını HTTP koduna çevirir (404 / 403 / 409 / 400).</summary>
    private IActionResult MapFailure(ResponseBaseDto result)
    {
        if (result.NotFound)
            return NotFound(result);
        if (result.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, result);
        if (result.Conflict)
            return Conflict(result);
        return BadRequest(result);
    }
}

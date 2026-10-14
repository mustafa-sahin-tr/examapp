using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Bookings;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #439 (epic #435): özel derste veli onayı. Gateway üzerinden <c>/api/exam/parent/...</c> (mevcut
/// <c>/api/exam/{everything}</c> wildcard route'u). Yalnız Parent rolü; sahiplik servis katmanında: çağıranın talebin öğrencisiyle
/// Active bağlantısı (birincil olması gerekmez) — aksi her durum 404 <c>{ message, errorCode: "NotFound" }</c>.
/// Çocuğa ait veri: yanıtlar önbelleğe alınmaz.
/// </summary>
[ApiController]
[Route("api/parent")]
[Authorize(Roles = "Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentBookingsController : BaseController
{
    private readonly IBookingService _bookings;
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentBookingsController(IBookingService bookings, IStringLocalizer<Messages>? localizer = null)
    {
        _bookings = bookings;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>
    /// "Onay bekleyen ders talepleri": velinin Active bağlantılı TÜM çocuklarının, hâlâ karar verilebilir AwaitingParent talepleri
    /// (ders zamanına göre artan, en fazla 200). Süresi dolmuş talepler dönmez.
    /// </summary>
    [HttpGet("booking-requests")]
    [ProducesResponseType(typeof(ParentBookingRequestListDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBookingRequests(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        return Ok(await _bookings.GetParentBookingRequestsAsync(user.Id, ct));
    }

    /// <summary>
    /// Talebi onaylar → Pending, öğretmene bildirim. 200 <see cref="BookingResultDto"/>. 404 bağlı olmayan çocuk / talep yok;
    /// 409 <c>AlreadyDecided</c> (başka veli önce karar verdi / süre aşımı / öğretmen askısı), <c>BookingRequestExpired</c>,
    /// <c>TeacherUnavailable</c>, <c>BookingRequestSlotDeleted</c> ya da ödeme kapısının kodu.
    /// </summary>
    [HttpPost("booking-requests/{id:int}/approve")]
    public async Task<IActionResult> ApproveBookingRequest(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var result = await _bookings.ParentApproveBookingAsync(user.Id, id, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>
    /// Talebi reddeder → ParentRejected (öğretmene hiç gitmez); gövde opsiyonel <c>{ note }</c> (en fazla 500 karakter, öğrenciye
    /// bildirilir). Hata kodları onaylamayla aynı (<c>TeacherUnavailable</c>/<c>BookingRequestSlotDeleted</c> hariç).
    /// </summary>
    [HttpPost("booking-requests/{id:int}/reject")]
    public async Task<IActionResult> RejectBookingRequest(int id, [FromBody] ParentRejectBookingDto? request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var result = await _bookings.ParentRejectBookingAsync(user.Id, id, request?.Note, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>
    /// Veli çocuğu adına doğrudan talep açar (öğretmen seçimi: <c>GET teacher/search</c>, <c>GET teacher/{id}/public-profile</c>,
    /// <c>GET booking/teachers/{teacherId}/slots</c>). AwaitingParent atlanır: Pending + ParentApprovedAt dolu, öğretmene bildirim.
    /// 201 <see cref="BookingResultDto"/>; 404 bağlı olmayan çocuk ya da slot yok; 409 slot dolu / meşgul; 400 geçmiş slot.
    /// </summary>
    [HttpPost("children/{studentId:int}/bookings")]
    public async Task<IActionResult> CreateBookingForChild(int studentId, [FromBody] CreateBookingDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var result = await _bookings.CreateBookingForChildAsync(user.Id, studentId, request, ct);
        if (!result.Success)
            return MapFailure(result);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Hata gövdesi: <c>{ message, errorCode }</c>; 404 / 403 / 409 / 400 (veli uçlarının ortak şekli).</summary>
    private IActionResult MapFailure(BookingResultDto result)
    {
        var body = new
        {
            message = result.Message,
            errorCode = result.ErrorCode ?? (result.NotFound ? BookingErrorCodes.NotFound : null)
        };
        if (result.NotFound)
            return NotFound(body);
        if (result.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, body);
        if (result.Conflict)
            return Conflict(body);
        return BadRequest(body);
    }
}

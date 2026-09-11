using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public BookingController(IBookingService bookingService) : base()
    {
        _bookingService = bookingService;
    }

    // ---------------- Müsaitlik slotları (öğretmen) ----------------

    /// <summary>Öğretmen kendi adına bir müsaitlik aralığı tanımlar.</summary>
    [HttpPost("slots")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> CreateSlot([FromBody] CreateAvailabilitySlotDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var result = await _bookingService.CreateSlotAsync(user.Id, request, ct);
        if (!result.Success)
            return MapFailure(result);

        return CreatedAtAction(nameof(GetMySlots), new { }, result);
    }

    /// <summary>Öğretmenin kendi müsaitlik aralıkları (geçmiş + gelecek), aktif randevu durumuyla.</summary>
    [HttpGet("slots/mine")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> GetMySlots([FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var result = await _bookingService.GetMySlotsAsync(user.Id, skip, take, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>Öğretmen kendi, aktif randevusu olmayan müsaitlik aralığını siler.</summary>
    [HttpDelete("slots/{id:int}")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> DeleteSlot(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var result = await _bookingService.DeleteSlotAsync(user.Id, id, ct);
        return result.Success ? NoContent() : MapFailure(result);
    }

    // ---------------- Müsaitlik slotları (öğrenci görünümü) ----------------

    /// <summary>Onaylı bir öğretmenin gelecekteki, henüz randevu alınmamış müsaitlik aralıkları.</summary>
    [HttpGet("teachers/{teacherId:int}/slots")]
    [Authorize]
    public async Task<IActionResult> GetTeacherSlots(int teacherId, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
    {
        var result = await _bookingService.GetTeacherOpenSlotsAsync(teacherId, skip, take, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    // ---------------- Randevu talepleri ----------------

    /// <summary>Öğrenci bir müsaitlik aralığı için randevu talebi oluşturur.</summary>
    [HttpPost("requests")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> CreateRequest([FromBody] CreateBookingDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var result = await _bookingService.CreateBookingAsync(user.Id, request, ct);
        if (!result.Success)
            return MapFailure(result);

        return CreatedAtAction(nameof(GetStudentRequests), new { }, result);
    }

    /// <summary>Öğretmene gelen randevu talepleri.</summary>
    [HttpGet("requests/teacher")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> GetTeacherRequests([FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

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
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var result = await _bookingService.GetStudentBookingsAsync(user.Id, skip, take, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>Öğretmen kendi slotuna gelen talebi onaylar.</summary>
    [HttpPost("requests/{id:int}/approve")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> ApproveRequest(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var result = await _bookingService.ApproveBookingAsync(user.Id, id, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>Öğretmen kendi slotuna gelen talebi reddeder. Gövde (gerekçe) opsiyoneldir.</summary>
    [HttpPost("requests/{id:int}/reject")]
    [Authorize(Roles = "Teacher")]
    public async Task<IActionResult> RejectRequest(int id, [FromBody] RejectBookingDto? request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var result = await _bookingService.RejectBookingAsync(user.Id, id, request?.RejectionReason, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    // ---------------- Görüşme odası (issue #97) ----------------

    /// <summary>
    /// Onaylı bir randevu için görüşme odası bilgisi + katılım token'ı döner. Hem öğretmen hem
    /// öğrenci aynı ucu çağırır; token'daki moderatör yetkisini servis katmanı belirler.
    /// </summary>
    [HttpPost("requests/{id:int}/video-session")]
    [Authorize(Roles = "Teacher,Student")]
    public async Task<IActionResult> CreateVideoSession(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync();
        if (user == null)
            return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");

        var result = await _bookingService.GetVideoSessionAsync(user.Id, id, ct);
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

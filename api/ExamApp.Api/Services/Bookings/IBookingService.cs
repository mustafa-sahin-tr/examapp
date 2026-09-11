using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Models.Dtos.Video;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// Ders planlama / randevu akışı (issue #96). Tüm metotlar çağıranın <c>userId</c>'sini alır ve
/// sahiplik kontrolünü kendi içinde yapar — controller sadece HTTP eşlemesi yapar.
/// </summary>
public interface IBookingService
{
    /// <summary>Öğretmen kendi adına müsaitlik aralığı ekler. Onaylı olmayan öğretmen slot tanımlayamaz (Forbidden).</summary>
    Task<AvailabilitySlotResultDto> CreateSlotAsync(int teacherUserId, CreateAvailabilitySlotDto dto, CancellationToken ct = default);

    /// <summary>Öğretmenin kendi slotları (geçmiş + gelecek), aktif booking durumuyla birlikte.</summary>
    Task<AvailabilitySlotListResultDto> GetMySlotsAsync(int teacherUserId, int skip, int take, CancellationToken ct = default);

    /// <summary>Öğretmen kendi slotunu siler. Aktif (Pending/Approved) booking'i varsa silinemez.</summary>
    Task<ResponseBaseDto> DeleteSlotAsync(int teacherUserId, int slotId, CancellationToken ct = default);

    /// <summary>Öğrenciye açık liste: onaylı öğretmenin gelecekteki, aktif booking'i olmayan slotları.</summary>
    Task<AvailabilitySlotListResultDto> GetTeacherOpenSlotsAsync(int teacherId, int skip, int take, CancellationToken ct = default);

    /// <summary>Öğrenci bir slot için randevu talebi oluşturur (çakışma + geçmiş tarih kontrolü burada).</summary>
    Task<BookingResultDto> CreateBookingAsync(int studentUserId, CreateBookingDto dto, CancellationToken ct = default);

    /// <summary>Öğretmene gelen randevu talepleri.</summary>
    Task<BookingListResultDto> GetTeacherBookingsAsync(int teacherUserId, int skip, int take, CancellationToken ct = default);

    /// <summary>Öğrencinin kendi randevu talepleri.</summary>
    Task<BookingListResultDto> GetStudentBookingsAsync(int studentUserId, int skip, int take, CancellationToken ct = default);

    /// <summary>Öğretmen kendi slotuna gelen talebi onaylar.</summary>
    Task<BookingResultDto> ApproveBookingAsync(int teacherUserId, int bookingId, CancellationToken ct = default);

    /// <summary>Öğretmen kendi slotuna gelen talebi reddeder; gerekçe opsiyonel.</summary>
    Task<BookingResultDto> RejectBookingAsync(int teacherUserId, int bookingId, string? rejectionReason, CancellationToken ct = default);

    /// <summary>
    /// Onaylı bir randevu için görüşme odası bilgisi + katılım token'ı üretir (issue #97).
    /// Çağıran randevunun öğretmeni veya öğrencisi olmalıdır (aksi halde Forbidden); randevu
    /// Approved değilse ya da katılım penceresi dışındaysa Conflict döner.
    /// </summary>
    Task<VideoSessionResultDto> GetVideoSessionAsync(int callerUserId, int bookingId, CancellationToken ct = default);
}

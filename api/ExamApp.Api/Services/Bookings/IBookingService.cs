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

    /// <summary>
    /// Öğretmen kendi slotunu siler. Aktif (Pending/Approved) booking'i varsa silinemez → Conflict +
    /// <see cref="BookingErrorCodes.SlotHasActiveBooking"/> (issue #376; kontrol + silme öğretmen müsaitlik kilidi altında).
    /// </summary>
    Task<AvailabilitySlotDeleteResultDto> DeleteSlotAsync(int teacherUserId, int slotId, CancellationToken ct = default);

    /// <summary>Öğrenciye açık liste: onaylı öğretmenin gelecekteki, aktif booking'i olmayan slotları.</summary>
    /// <remarks>issue #418: yalnız bağımsız öğretmen; okula bağlı öğretmen (aynı okul/admin dahil) → NotFound.</remarks>
    Task<AvailabilitySlotListResultDto> GetTeacherOpenSlotsAsync(int teacherId, int skip, int take, CancellationToken ct = default);

    /// <summary>
    /// Öğrenci bir slot için randevu talebi oluşturur (çakışma + geçmiş tarih kontrolü burada). issue #439:
    /// <paramref name="requiresParent"/> = <c>ParentRequirement.RequiresParent(User)</c>. true ise talep AwaitingParent açılır
    /// (öğretmene gitmez, velilere bildirim); Active velisi yoksa Forbidden + <c>ParentRequired</c>. false ise eski akış (Pending).
    /// </summary>
    Task<BookingResultDto> CreateBookingAsync(int studentUserId, CreateBookingDto dto, bool requiresParent, CancellationToken ct = default);

    /// <summary>
    /// issue #439: veli, Active bağlantılı çocuğu adına doğrudan talep açar — AwaitingParent atlanır (Pending, ParentApprovedAt
    /// dolu), öğretmene bildirim. Bağlı olmayan çocuk NotFound. Slot/öğretmen kuralları öğrenci talebiyle aynı.
    /// </summary>
    Task<BookingResultDto> CreateBookingForChildAsync(int parentUserId, int studentId, CreateBookingDto dto, CancellationToken ct = default);

    /// <summary>issue #439: velinin Active bağlantılı çocuklarının, hâlâ karar verilebilir AwaitingParent talepleri.</summary>
    Task<ParentBookingRequestListDto> GetParentBookingRequestsAsync(int parentUserId, CancellationToken ct = default);

    /// <summary>
    /// issue #439: veli AwaitingParent talebi onaylar → Pending (öğretmene bildirim). Bağlı olmayan veli NotFound; başka karar önce
    /// geldiyse Conflict + <c>AlreadyDecided</c>; süresi dolduysa <c>BookingRequestExpired</c>; öğretmen artık talep alamıyorsa
    /// <c>TeacherUnavailable</c>.
    /// </summary>
    Task<BookingResultDto> ParentApproveBookingAsync(int parentUserId, int bookingId, CancellationToken ct = default);

    /// <summary>issue #439: veli AwaitingParent talebi reddeder (opsiyonel not, en fazla 500) → ParentRejected.</summary>
    Task<BookingResultDto> ParentRejectBookingAsync(int parentUserId, int bookingId, string? note, CancellationToken ct = default);

    /// <summary>Öğretmene gelen randevu talepleri (issue #439: AwaitingParent / ParentRejected / Expired hariç).</summary>
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

    /// <summary>
    /// Canlı ders oturumu katılım kararı — görüşme odası (#97) ve ortak çizim tahtası (#98) aynı kuralı paylaşır:
    /// çağıran randevunun öğretmeni/öğrencisi, randevu Approved ve şimdi <see cref="BookingSessionWindow"/> içinde.
    /// Öğretmen hesabının onay/askı durumu (#287/#289) çağıranın sorumluluğundadır.
    /// </summary>
    Task<BookingLiveSessionAccess> GetLiveSessionAccessAsync(int callerUserId, int bookingId, CancellationToken ct = default);

    /// <summary>
    /// issue #315: randevuyu iptal eder. Öğrenci Pending/AwaitingParent/Approved, öğretmen Approved iptal edebilir; onaylı randevu
    /// başlangıca <c>BookingPolicy:CancellationMinNoticeHours</c> kalana kadar. Taraf değilse NotFound.
    /// </summary>
    Task<BookingResultDto> CancelBookingAsync(int callerUserId, int bookingId, string? reason, CancellationToken ct = default);

    /// <summary>issue #315: onaylı randevu için aynı öğretmenin boş bir slotunu erteleme olarak önerir.</summary>
    Task<BookingResultDto> ProposeRescheduleAsync(int callerUserId, int bookingId, int newSlotId, CancellationToken ct = default);

    /// <summary>
    /// issue #315: bekleyen erteleme önerisine yanıt. Karşı taraf kabul (<paramref name="accept"/>=true → randevu yeni slota
    /// taşınır) ya da ret eder; öneren taraf yalnız geri çekebilir (false).
    /// </summary>
    Task<BookingResultDto> RespondRescheduleAsync(int callerUserId, int bookingId, bool accept, CancellationToken ct = default);

    /// <summary>issue #315: ders bitişinden sonra karşı tarafın derse katılmadığını bildirir (ilk bildirim kalıcı).</summary>
    Task<BookingResultDto> ReportNoShowAsync(int callerUserId, int bookingId, CancellationToken ct = default);
}

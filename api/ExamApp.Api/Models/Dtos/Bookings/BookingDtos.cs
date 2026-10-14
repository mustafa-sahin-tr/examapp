using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos.Bookings;

/// <summary>POST /api/booking/slots — öğretmen kendi adına tek bir müsaitlik aralığı tanımlar (issue #96).</summary>
public class CreateAvailabilitySlotDto
{
    /// <summary>Aralığın günü. JSON'da "2026-09-20" formatında.</summary>
    [Required]
    public DateOnly Date { get; set; }

    /// <summary>Başlangıç saati. JSON'da "14:00:00" formatında.</summary>
    [Required]
    public TimeOnly StartTime { get; set; }

    [Required]
    public TimeOnly EndTime { get; set; }
}

/// <summary>
/// Müsaitlik aralığı görünümü. Öğretmenin kendi listesinde aktif booking bilgisi de dolu gelir;
/// öğrenciye dönen listede (yalnızca boş slotlar) <see cref="BookingId"/> null'dır.
/// </summary>
public class AvailabilitySlotDto
{
    public int Id { get; set; }
    public int TeacherId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Slotun başlangıcı UTC olarak (Date + StartTime). UI'ın tarih hesabı yapmasını kolaylaştırır.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>
    /// Slotun bitişi UTC olarak. <see cref="EndTime"/> &lt; <see cref="StartTime"/> ise (gün aşan slot, issue #300)
    /// ertesi günün saatidir; her zaman <see cref="StartUtc"/>'den büyüktür.
    /// </summary>
    public DateTime EndUtc { get; set; }

    /// <summary>
    /// Aktif (Pending, Approved ya da issue #439 ile veli onayı bekleyen AwaitingParent) bir booking var mı — varsa slot yeni
    /// talebe kapalıdır ve silinemez.
    /// </summary>
    public bool IsBooked { get; set; }

    /// <summary>Aktif booking varsa kimliği. issue #439: AwaitingParent talepte öğretmene verilmez (null).</summary>
    public int? BookingId { get; set; }

    /// <summary>"Pending" | "Approved" | "AwaitingParent" (issue #439: slot tutuluyor, talep henüz öğretmene ulaşmadı) — aktif booking yoksa null.</summary>
    public string? BookingStatus { get; set; }

    /// <summary>Aktif booking'i oluşturan öğrencinin adı (yalnızca öğretmenin kendi listesinde dolu).</summary>
    public string? StudentName { get; set; }

    /// <summary>
    /// Slot bir tekrarlayan kuraldan üretildiyse kuralın kimliği (issue #178); tekil slotta null.
    /// Yalnızca öğretmenin kendi listesinde (slots/mine) dolar; öğrenciye dönen listede her zaman null.
    /// UI "tekrarlayan" ikonunu ve silme diyalogundaki "sadece bu hafta / tüm seri" seçimini buna göre gösterir.
    /// </summary>
    public int? RecurringAvailabilityRuleId { get; set; }
}

/// <summary>
/// POST /api/booking/recurring-rules — öğretmen "her hafta tekrarla" kuralı tanımlar (issue #178).
/// Saatler tekil slotlarla aynı şekilde saat dilimsiz duvar saatidir ve UTC kabul edilir.
/// </summary>
public class CreateRecurringAvailabilityRuleDto
{
    /// <summary>0=Pazar .. 6=Cumartesi (System.DayOfWeek). JSON'da sayı olarak gönderilir.</summary>
    [Required]
    [EnumDataType(typeof(DayOfWeek), ErrorMessage = "booking.recurringRule.invalidDayOfWeek")]
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Başlangıç saati. JSON'da "14:00:00" formatında; dakika hassasiyeti (saniye 0). Süre 30 dk – 4 saat.</summary>
    [Required]
    public TimeOnly StartTime { get; set; }

    [Required]
    public TimeOnly EndTime { get; set; }

    /// <summary>Kuralın geçerli olduğu ilk gün (dahil). JSON'da "2026-09-28". UTC bugün .. bugün+90 gün.</summary>
    [Required]
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Kuralın geçerli olduğu son gün (dahil). Null = süresiz; verilirse EffectiveFrom'dan sonra ve en fazla 1 yıl ileride.</summary>
    public DateOnly? EffectiveUntil { get; set; }
}

/// <summary>Tekrarlayan kural görünümü.</summary>
public class RecurringAvailabilityRuleDto
{
    public int Id { get; set; }
    public int TeacherId { get; set; }

    /// <summary>0=Pazar .. 6=Cumartesi.</summary>
    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Kural oluşturma sonucu: kural + ufuk (90 gün) içinde üretilen somut slot id'leri + çakışma
/// nedeniyle atlanan tarihler (hata değil; öğretmen o haftaları elle düzenler).
/// </summary>
public class RecurringAvailabilityRuleResultDto : ResponseBaseDto
{
    public RecurringAvailabilityRuleDto? Rule { get; set; }

    public List<int> GeneratedSlotIds { get; set; } = new();

    public List<DateOnly> SkippedDates { get; set; } = new();
}

/// <summary>GET /api/booking/recurring-rules/mine sonucu.</summary>
public class RecurringAvailabilityRuleListResultDto : ResponseBaseDto
{
    public List<RecurringAvailabilityRuleDto> Items { get; set; } = new();
}

/// <summary>
/// DELETE /api/booking/recurring-rules/{id} sonucu ("tüm seri"): kural pasifleşti; aktif randevusu
/// olmayan gelecek slotlar silindi, randevulu olanlar korundu.
/// </summary>
public class RecurringAvailabilityRuleDeleteResultDto : ResponseBaseDto
{
    /// <summary>Soft-delete edilen gelecek slot id'leri.</summary>
    public List<int> DeletedSlotIds { get; set; } = new();

    /// <summary>Aktif (Pending/Approved) randevusu olduğu için dokunulmayan slot id'leri.</summary>
    public List<int> PreservedSlotIds { get; set; } = new();

    /// <summary><see cref="PreservedSlotIds"/> sayısı — UI'ın "N randevulu slot korundu" mesajı için.</summary>
    public int PreservedBookedCount { get; set; }
}

/// <summary>
/// DELETE /api/booking/slots/{id} hata gövdesi (başarıda 204, gövde yok). issue #376: aktif randevulu slot reddinde
/// makine okunur <see cref="ErrorCode"/> taşır; diğer hatalarda alan JSON'a çıkmaz.
/// </summary>
public class AvailabilitySlotDeleteResultDto : ResponseBaseDto
{
    /// <summary>Bkz. <see cref="BookingErrorCodes"/>. Yalnız 409 aktif-randevu reddinde dolu.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }
}

/// <summary>Randevu/müsaitlik uçlarının hata gövdelerindeki <c>errorCode</c> değerleri (UI özel davranış için okur).</summary>
public static class BookingErrorCodes
{
    /// <summary>
    /// issue #376: slotun aktif (Pending/Approved) randevusu var → slot silinemez (409). Tekil silmede döner; seri silme
    /// (<c>DELETE recurring-rules/{id}</c>) bu slotları hata vermeden korur (<c>preservedSlotIds</c>).
    /// </summary>
    public const string SlotHasActiveBooking = "SlotHasActiveBooking";

    /// <summary>
    /// issue #376: Pending talebin slotu soft-delete edilmiş (eski veri) → talep onaylanamaz, yalnız reddedilebilir (409,
    /// <c>POST requests/{id}/approve</c>).
    /// </summary>
    public const string RequestSlotDeleted = "BookingRequestSlotDeleted";

    /// <summary>
    /// issue #439: veli gerektiren öğrencinin Active bağlantılı velisi yok → randevu talebi oluşturamaz (403,
    /// <c>POST requests</c>). UI öğrenciyi veli bağlama akışına yönlendirir.
    /// </summary>
    public const string ParentRequired = "ParentRequired";

    /// <summary>
    /// issue #439: veli onayı bekleyen talep başka bir veli (ya da süre aşımı / öğretmen müsait değil) tarafından zaten
    /// sonuçlandırıldı (409, <c>POST parent/booking-requests/{id}/approve|reject</c>). İlk karar kazanır.
    /// </summary>
    public const string AlreadyDecided = "AlreadyDecided";

    /// <summary>
    /// issue #439: veli onayı süresi doldu (oluşturmadan 7 gün geçti ya da ders saati geldi) → onay/ret yapılamaz (409). Talep
    /// süpürme işiyle Expired'a çekilir.
    /// </summary>
    public const string RequestExpired = "BookingRequestExpired";

    /// <summary>
    /// issue #439: veli onayı sırasında öğretmen artık talep alamıyor (askıda, onayı kalkmış ya da bağımsız değil) → onaylanamaz
    /// (409). Askı / bağımsızlık kaybı durumunda talep aynı istekte "öğretmen müsait değil" gerekçesiyle kapatılır.
    /// </summary>
    public const string TeacherUnavailable = "TeacherUnavailable";

    /// <summary>issue #439: veli bağlantısı yok / talep bulunamadı (veli uçları; varlık sızdırılmaz).</summary>
    public const string NotFound = "NotFound";

    /// <summary>
    /// issue #439 review: öğrencinin açık AwaitingParent talep tavanı aşıldı — toplam 3, aynı öğretmen için 1 (409,
    /// <c>POST requests</c>). Veli karar verince / talep süresi dolunca yeni talep açılabilir.
    /// </summary>
    public const string TooManyAwaitingParent = "TooManyAwaitingParent";

    /// <summary>issue #439 review: veli ret notunda izin verilmeyen karakter (NUL/kontrol karakteri) → 400.</summary>
    public const string InvalidNote = "InvalidNote";
}

/// <summary>Randevu karar (onay/ret) sonucu; issue #376 ile makine okunur <see cref="BookingResultDto.ErrorCode"/> taşır.</summary>
public class BookingDecisionResultDto : BookingResultDto
{
}

/// <summary>POST /api/booking/requests — öğrenci bir slot için randevu talebi oluşturur.</summary>
public class CreateBookingDto
{
    [Range(1, int.MaxValue, ErrorMessage = "booking.request.slotIdRequired")]
    public int AvailabilitySlotId { get; set; }
}

/// <summary>POST /api/booking/requests/{id}/reject gövdesi. Gerekçe opsiyoneldir.</summary>
public class RejectBookingDto
{
    [MaxLength(500, ErrorMessage = "booking.request.rejectionReasonTooLong")]
    public string? RejectionReason { get; set; }
}

/// <summary>Randevu talebi görünümü. Hem öğretmen hem öğrenci listelerinde aynı şekil kullanılır.</summary>
public class BookingDto
{
    public int Id { get; set; }

    public int TeacherId { get; set; }

    /// <summary>auth-api'den çözümlenir; erişilemezse null.</summary>
    public string? TeacherName { get; set; }

    public int StudentId { get; set; }

    public string? StudentName { get; set; }

    public int AvailabilitySlotId { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>Randevunun başlangıcı UTC olarak (Date + StartTime).</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Randevunun bitişi UTC olarak; gün aşan slotta ertesi gün (issue #300). Her zaman &gt; <see cref="StartUtc"/>.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>
    /// "Pending" | "Approved" | "Rejected"; issue #439 ile öğrenci/veli görünümünde ayrıca "AwaitingParent" | "Expired" |
    /// "ParentRejected" (öğretmen listesinde bu üçü hiç yer almaz).
    /// </summary>
    public string Status { get; set; } = nameof(BookingStatus.Pending);

    public DateTime CreatedAt { get; set; }
    public DateTime? DecisionAt { get; set; }

    /// <summary>Rejected'da öğretmenin, ParentRejected'da velinin notu.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>issue #439: veli onay anı (UTC); veli onayından geçmemiş talepte null.</summary>
    public DateTime? ParentApprovedAt { get; set; }

    /// <summary>
    /// issue #439: yalnız Status=AwaitingParent iken dolu — bu andan sonra talep otomatik Expired olur (UTC; oluşturma + 7 gün
    /// ya da ders başlangıcı, hangisi önceyse).
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// issue #439: veli panelinde "Onay bekleyen ders talepleri" satırı (<c>GET /api/parent/booking-requests</c>). Velinin Active
/// bağlantılı çocuklarının AwaitingParent talepleri. Ücret/not/görüşme bilgisi yok.
/// </summary>
public class ParentBookingRequestDto
{
    public int Id { get; set; }
    public int StudentId { get; set; }

    /// <summary>auth-api'den çözülür; erişilemezse null.</summary>
    public string? StudentName { get; set; }

    public int TeacherId { get; set; }
    public string? TeacherName { get; set; }
    public int AvailabilitySlotId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Bu andan sonra talep otomatik Expired olur (UTC).</summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary><c>GET /api/parent/booking-requests</c> gövdesi.</summary>
public class ParentBookingRequestListDto
{
    public List<ParentBookingRequestDto> Items { get; set; } = new();
}

/// <summary><c>POST /api/parent/booking-requests/{id}/reject</c> gövdesi (opsiyonel). Not öğrenciye bildirilir.</summary>
public class ParentRejectBookingDto
{
    [MaxLength(500, ErrorMessage = "booking.parent.noteTooLong")]
    public string? Note { get; set; }
}

/// <summary>Servisten controller'a tekil slot sonucu; ResponseBaseDto bayrakları HTTP koduna eşlenir.</summary>
public class AvailabilitySlotResultDto : ResponseBaseDto
{
    public AvailabilitySlotDto? Slot { get; set; }
}

/// <summary>Servisten controller'a tekil booking sonucu.</summary>
public class BookingResultDto : ResponseBaseDto
{
    public BookingDto? Booking { get; set; }

    /// <summary>Bkz. <see cref="BookingErrorCodes"/>. Yalnız belirli hatalarda dolu; diğer yanıtlarda JSON'a çıkmaz.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }
}

/// <summary>Liste sonucu — çağıranın öğretmen/öğrenci kaydı yoksa NotFound bayrağı dolar.</summary>
public class AvailabilitySlotListResultDto : ResponseBaseDto
{
    public List<AvailabilitySlotDto> Items { get; set; } = new();
}

/// <summary>Liste sonucu — çağıranın öğretmen/öğrenci kaydı yoksa NotFound bayrağı dolar.</summary>
public class BookingListResultDto : ResponseBaseDto
{
    public List<BookingDto> Items { get; set; } = new();
}

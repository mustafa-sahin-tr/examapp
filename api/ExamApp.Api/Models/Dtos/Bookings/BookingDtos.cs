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

    public DateTime EndUtc { get; set; }

    /// <summary>Aktif (Pending veya Approved) bir booking var mı — varsa slot yeni talebe kapalıdır.</summary>
    public bool IsBooked { get; set; }

    /// <summary>Aktif booking varsa kimliği.</summary>
    public int? BookingId { get; set; }

    /// <summary>"Pending" | "Approved" — aktif booking yoksa null.</summary>
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

    public DateTime EndUtc { get; set; }

    /// <summary>"Pending" | "Approved" | "Rejected"</summary>
    public string Status { get; set; } = nameof(BookingStatus.Pending);

    public DateTime CreatedAt { get; set; }
    public DateTime? DecisionAt { get; set; }
    public string? RejectionReason { get; set; }
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

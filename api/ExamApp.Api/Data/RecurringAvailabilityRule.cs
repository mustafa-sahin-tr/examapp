using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Ders planlama (issue #178): öğretmenin "her hafta tekrarla" olarak işaretlediği müsaitlik deseni.
/// Bir kural = tek bir haftanın günü x tek bir saat aralığı. Kural kendisi randevu alınabilir bir
/// şey değildir; <see cref="Booking"/> somut bir <see cref="TeacherAvailabilitySlot"/> satırına zorunlu
/// FK ile bağlı olduğundan kural, kapsadığı her hafta için gerçek slot satırı üretir (materialize).
/// <para>
/// <see cref="StartTime"/>/<see cref="EndTime"/> saat dilimsiz duvar saatidir ve tekil slotlarla aynı
/// şekilde UTC kabul edilir. Materialize ufku <c>BookingService.MaxAdvanceDays</c> (90 gün) ile sınırlıdır;
/// pencere <c>GET /api/booking/slots/mine</c> çağrısında lazy olarak ileri kaydırılır.
/// </para>
/// </summary>
public class RecurringAvailabilityRule : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TeacherId { get; set; }

    [ForeignKey(nameof(TeacherId))]
    public Teacher Teacher { get; set; } = default!;

    /// <summary>Haftanın günü (0=Pazar .. 6=Cumartesi, <see cref="System.DayOfWeek"/>). DB'de int.</summary>
    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    /// <summary>Kuralın geçerli olduğu ilk gün (bu gün dahil).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Kuralın geçerli olduğu son gün (dahil). Null = süresiz.</summary>
    public DateOnly? EffectiveUntil { get; set; }

    /// <summary>"Tüm seriyi durdur" bunu false yapar; pasif kural top-up'ta yeni slot üretmez.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Bu kuraldan üretilmiş somut slotlar (soft-delete edilmişler dahil, sorgu filtresi hariç tutar).</summary>
    public ICollection<TeacherAvailabilitySlot> GeneratedSlots { get; set; } = new List<TeacherAvailabilitySlot>();
}

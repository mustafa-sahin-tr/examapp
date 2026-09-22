using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Ders planlama (issue #96): öğretmenin müsaitlik aralığı. Her aralık ayrı satırdır; satır ya
/// öğretmen tarafından tek tek tanımlanmıştır (<see cref="RecurringAvailabilityRuleId"/> null) ya da
/// bir <see cref="RecurringAvailabilityRule"/>'dan üretilmiştir (issue #178).
/// <para>
/// <see cref="Date"/>/<see cref="StartTime"/>/<see cref="EndTime"/> saat dilimsiz duvar saatidir
/// (PostgreSQL <c>date</c> + <c>time</c>). Geçmiş kontrolü ve takvim dönüşümü bunları UTC kabul eder;
/// çok bölgeli kullanım gerekirse ayrı bir issue'da timezone alanı eklenmelidir.
/// </para>
/// </summary>
public class TeacherAvailabilitySlot : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TeacherId { get; set; }

    [ForeignKey(nameof(TeacherId))]
    public Teacher Teacher { get; set; } = default!;

    /// <summary>Aralığın günü (saat bilgisi yok).</summary>
    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    /// <summary>Slotun oluşturulduğu an (UTC). BaseEntity.CreateTime ile aynı değeri taşır, DTO'ya bu alan gider.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Slot bir tekrarlayan kuraldan üretildiyse kuralın kimliği; tekil slotta null. Kural soft-delete
    /// edilse bile referans kalır — top-up sweep'i "bu kural için bu tarihe satır üretildi mi?"
    /// sorusunu (soft-delete edilmiş satırlar dahil) bu alanla yanıtlar.
    /// </summary>
    public int? RecurringAvailabilityRuleId { get; set; }

    [ForeignKey(nameof(RecurringAvailabilityRuleId))]
    public RecurringAvailabilityRule? RecurringAvailabilityRule { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

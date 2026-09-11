using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Ders planlama (issue #96): öğretmenin tek tek tanımladığı, tekrar etmeyen (non-recurring)
/// müsaitlik aralığı. MVP'de haftalık şablon yok — her aralık ayrı satırdır.
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

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>Randevu talebinin yaşam döngüsü (issue #96). İptal/erteleme MVP kapsamı dışında.</summary>
public enum BookingStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// Ders planlama (issue #96): öğrencinin bir <see cref="TeacherAvailabilitySlot"/> için oluşturduğu
/// randevu talebi. Bir slota aynı anda yalnızca bir aktif (Pending/Approved) booking olabilir —
/// bu kısıt AppDbContext'te filtreli unique index ile DB seviyesinde de garanti edilir.
/// </summary>
public class Booking : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>Slotun sahibi öğretmen. Slot üzerinden türetilebilir; sorgu kolaylığı için denormalize tutulur.</summary>
    [Required]
    public int TeacherId { get; set; }

    [ForeignKey(nameof(TeacherId))]
    public Teacher Teacher { get; set; } = default!;

    [Required]
    public int StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public Student Student { get; set; } = default!;

    [Required]
    public int AvailabilitySlotId { get; set; }

    [ForeignKey(nameof(AvailabilitySlotId))]
    public TeacherAvailabilitySlot AvailabilitySlot { get; set; } = default!;

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    /// <summary>Talebin oluşturulduğu an (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Onay/ret kararının verildiği an (UTC). Pending iken null.</summary>
    public DateTime? DecisionAt { get; set; }

    /// <summary>Öğretmenin ret gerekçesi (opsiyonel). Sadece Status=Rejected iken dolu olabilir.</summary>
    [MaxLength(500)]
    public string? RejectionReason { get; set; }
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>Randevu talebinin yaşam döngüsü (issue #96). İptal/erteleme MVP kapsamı dışında.</summary>
/// <remarks>
/// issue #439: veli onayı. Veli gerektiren öğrencinin talebi <see cref="AwaitingParent"/> ile başlar (slotu tutar, öğretmene
/// görünmez); veli onaylarsa <see cref="Pending"/> olur ve eski öğretmen akışına girer, reddederse <see cref="ParentRejected"/>,
/// 7 gün içinde (ya da ders saatine kadar) karar verilmezse <see cref="Expired"/>. Değerler DB'de int olarak saklanır.
/// </remarks>
public enum BookingStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,

    /// <summary>issue #439: veli onayı bekliyor — slotu tutar, öğretmen listesine/bildirimine düşmez.</summary>
    AwaitingParent = 3,

    /// <summary>issue #439: veli onayı süresinde gelmedi (oluşturmadan 7 gün sonra ya da ders saati geldiğinde).</summary>
    Expired = 4,

    /// <summary>issue #439: veli reddetti (öğretmene hiç ulaşmadı).</summary>
    ParentRejected = 5
}

/// <summary>
/// Ders planlama (issue #96): öğrencinin bir <see cref="TeacherAvailabilitySlot"/> için oluşturduğu
/// randevu talebi. Bir slota aynı anda yalnızca bir aktif (AwaitingParent/Pending/Approved) booking olabilir —
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

    /// <summary>
    /// Ret gerekçesi (opsiyonel). Status=Rejected iken öğretmenin, Status=ParentRejected iken velinin notu (issue #439).
    /// </summary>
    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    /// <summary>
    /// issue #439: velinin onay anı (UTC). Veli talebi onayladığında ya da talebi veli doğrudan oluşturduğunda dolar; eski
    /// (veli onayı öncesi) talepler ve veli gerektirmeyen hesapların talepleri için null.
    /// </summary>
    public DateTime? ParentApprovedAt { get; set; }

    /// <summary>issue #439: onaylayan / talebi oluşturan velinin exam/auth user id'si (<c>Parent.UserId</c>).</summary>
    public int? ParentApprovedBy { get; set; }

    /// <summary>issue #439: talebi reddeden velinin exam/auth user id'si (<c>Parent.UserId</c>); karar anı <see cref="DecisionAt"/>.</summary>
    public int? ParentRejectedBy { get; set; }
}

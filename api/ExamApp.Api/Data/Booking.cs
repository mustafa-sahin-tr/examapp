using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Randevu talebinin yaşam döngüsü (issue #96). issue #315: taraflardan biri iptal edebilir (<see cref="Cancelled"/>);
/// erteleme durum değiştirmez (onaylı randevunun slotu karşı taraf kabul edince değişir), no-show ayrı alanlarda tutulur.
/// </summary>
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
    ParentRejected = 5,

    /// <summary>
    /// issue #315: öğrenci (Pending/Approved/AwaitingParent) ya da öğretmen (Approved) iptal etti; slot müsaitliğe döner.
    /// Kim/ne zaman/neden: <see cref="Booking.CancelledByRole"/>, <see cref="Booking.CancelledAt"/>,
    /// <see cref="Booking.CancellationReason"/>.
    /// </summary>
    Cancelled = 6
}

/// <summary>issue #315: iptal / erteleme / no-show alanlarında tarafın rolü (DB'de string).</summary>
public static class BookingParty
{
    public const string Teacher = "Teacher";
    public const string Student = "Student";
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

    // ---------------- issue #315: iptal ----------------

    /// <summary>İptal anı (UTC); Status=Cancelled iken dolu.</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>İptal eden kullanıcının exam/auth user id'si.</summary>
    public int? CancelledByUserId { get; set; }

    /// <summary><see cref="BookingParty"/> değeri.</summary>
    [MaxLength(16)]
    public string? CancelledByRole { get; set; }

    /// <summary>Opsiyonel iptal gerekçesi (karşı tarafa bildirilir).</summary>
    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    /// <summary>
    /// İptal edilmeden önceki durum. Öğretmene hiç ulaşmamış (AwaitingParent) talebin iptali öğretmen listesinde görünmez.
    /// </summary>
    public BookingStatus? CancelledFromStatus { get; set; }

    // ---------------- issue #315: erteleme önerisi ----------------

    /// <summary>
    /// Bekleyen erteleme önerisinin yeni slotu (aynı öğretmenin boş slotu). Karşı taraf kabul edene kadar randevu eski slotta
    /// kalır; öneri slotu TUTMAZ (kabulde boş olduğu yeniden doğrulanır).
    /// </summary>
    public int? ProposedSlotId { get; set; }

    [ForeignKey(nameof(ProposedSlotId))]
    public TeacherAvailabilitySlot? ProposedSlot { get; set; }

    /// <summary>Öneriyi yapan tarafın <see cref="BookingParty"/> değeri.</summary>
    [MaxLength(16)]
    public string? ProposedByRole { get; set; }

    public int? ProposedByUserId { get; set; }

    public DateTime? ProposedAt { get; set; }

    /// <summary>Kabul edilmiş erteleme sayısı (<c>BookingPolicy:MaxReschedulesPerBooking</c> sınırı).</summary>
    public int RescheduleCount { get; set; }

    /// <summary>Yapılan erteleme önerisi sayısı — kabul/ret/geri çekme fark etmez (<c>BookingPolicy:MaxProposalsPerBooking</c>).</summary>
    public int RescheduleProposalCount { get; set; }

    // ---------------- issue #315: no-show ----------------

    /// <summary>Derse katılmadığı bildirilen taraf (<see cref="BookingParty"/>); bildirilmediyse null.</summary>
    [MaxLength(16)]
    public string? NoShowParty { get; set; }

    public int? NoShowReportedByUserId { get; set; }

    public DateTime? NoShowReportedAt { get; set; }
}

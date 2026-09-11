using System;

namespace ExamApp.Api.Data;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Öğretmen hesabının onay durumu (issue #92). Okula bağlı öğretmenler doğrudan Approved;
/// bağımsız öğretmenler (IsIndependentTutor) admin onayı bekler.
/// </summary>
public enum TeacherApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class Teacher : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    // 🟢 Geçiş dönemi: register akışı artık SchoolId kullanıyor, bu alan legacy veri için nullable.
    [MaxLength(100)]
    public string? SchoolName { get; set; }

    // 🟢 Geçiş dönemi: SchoolId nullable, mevcut kayıtlar SchoolName ile kalmaya devam eder.
    public int? SchoolId { get; set; }

    [ForeignKey("SchoolId")]
    public School? School { get; set; }

    /// <summary>Okula bağlı olmayan, bağımsız çalışan öğretmen (issue #92). SchoolId null olabilir.</summary>
    public bool IsIndependentTutor { get; set; }

    /// <summary>Okula bağlı öğretmen için varsayılan Approved; bağımsız öğretmen kayıtta Pending başlar.</summary>
    public TeacherApprovalStatus ApprovalStatus { get; set; } = TeacherApprovalStatus.Approved;

    /// <summary>Admin başvuruyu reddettiğinde girdiği neden (issue #94). Sadece ApprovalStatus=Rejected iken dolu.</summary>
    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    [MaxLength(20)]
    public string? ThemePreset { get; set; } = "standard"; // 🎨 Theme tercihi (minimal, standard, enhanced, full)

    public string? ThemeCustomConfig { get; set; } // 🎨 Custom theme config (JSON format)

    // ---- Bağımsız öğretmen (tutor) profil alanları (issue #95) ----

    /// <summary>Saatlik ücret — sadece bilgi amaçlı, ödeme akışı yok. Profil doldurulmadıysa null.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal? HourlyRate { get; set; }

    /// <summary>Online ders veriyor mu. Profil kaydında en az biri (Online/InPerson) true olmalı.</summary>
    public bool TeachesOnline { get; set; }

    /// <summary>Yüz yüze ders veriyor mu.</summary>
    public bool TeachesInPerson { get; set; }

    /// <summary>Kısa tanıtım metni (öğrenci arama sonuçlarında ve public profilde gösterilir).</summary>
    [MaxLength(500)]
    public string? Bio { get; set; }

    /// <summary>Verdiği dersler (curriculum Subject tablosu ile many-to-many).</summary>
    public ICollection<TeacherSubject> TeacherSubjects { get; set; } = new List<TeacherSubject>();
}

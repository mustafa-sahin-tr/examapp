using System;

namespace ExamApp.Api.Data;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Öğretmen hesabının onay durumu (issue #92). Bağımsız öğretmenler (IsIndependentTutor) ve okul bağlantısı
/// talep eden öğretmenler (<see cref="Teacher.RequestedSchoolId"/>, issue #234) admin onayı bekler.
/// </summary>
public enum TeacherApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class Teacher : BaseEntity, ISchoolScoped
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

    /// <summary>
    /// issue #234: kayıtta öğretmenin bağlanmak İSTEDİĞİ okul. Kullanıcı okul üyeliğini kendisi kuramaz;
    /// istek burada Pending bekler, <see cref="SchoolId"/> admin onayına kadar null kalır — böylece okul-kapsam
    /// kuralları (<c>ISchoolAccessPolicy</c>, #190/#192/#222) onu okulsuz görür. Onayda SchoolId'ye taşınır ve
    /// temizlenir; redde SchoolId kurulmaz, RequestedSchoolId hangi talebin reddedildiğini göstermek için kalır.
    /// Bekleyen talep = <c>RequestedSchoolId != null &amp;&amp; ApprovalStatus == Pending</c>.
    /// </summary>
    public int? RequestedSchoolId { get; set; }

    public School? RequestedSchool { get; set; }

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

    /// <summary>
    /// Kayıt test verisi aracı (<c>seed-teachers</c>, issue #217) tarafından mı oluşturuldu?
    /// Gerçek öğretmenlerden ayırt etmek ve toplu temizlemek (issue #218) için. Normal register
    /// akışıyla açılan kayıtlarda false.
    /// </summary>
    public bool IsSeedData { get; set; }
}

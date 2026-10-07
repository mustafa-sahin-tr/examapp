using System;

namespace ExamApp.Api.Data;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Student : BaseEntity, ISchoolScoped
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required, MaxLength(50)]
    public string StudentNumber { get; set; }

    // 🟢 Geçiş dönemi: register akışı artık SchoolId kullanıyor, bu alan legacy veri için nullable.
    [MaxLength(100)]
    public string? SchoolName { get; set; }

    // 🟢 Geçiş dönemi: SchoolId nullable, mevcut kayıtlar SchoolName ile kalmaya devam eder.
    public int? SchoolId { get; set; }

    [ForeignKey("SchoolId")]
    public School? School { get; set; }

    /// <summary>
    /// issue #361: okul üyeliğinin doğrulandığı an (UTC). null = üyelik BEKLEMEDE (öğrenci kendi kaydında okul seçti) ya da
    /// okulsuz. Doğrulanmamış üyelik HİÇBİR okul kapsamlı yetki vermez — okul kararları <see cref="VerifiedSchoolId"/>
    /// (SQL'de <c>SchoolVerifiedAt != null ? SchoolId : null</c>) üzerinden verilir. Doğrulama yolları: admin okul ataması
    /// (PUT admin/students/{id}/school) ya da okulun onaylı öğretmeni/platform admin'inin başvuru onayı. Migration
    /// <c>AddStudentSchoolVerification</c> mevcut okullu öğrencileri doğrulanmış sayar.
    /// </summary>
    public DateTime? SchoolVerifiedAt { get; set; }

    /// <summary>
    /// issue #422 (security review): son ilerleme sıfırlamasının çizgisi (UTC, <c>StudentResetJob</c>'un BadgeService'e gönderdiği
    /// <c>resetAtUtc</c>). Bu andan (5 dk tolerans) önce kazanılmış bir rozetin geç teslim edilen <c>StudentBadgeEarnedEvent</c>'i
    /// projeksiyona yazılmaz. Hiç sıfırlanmamışsa null.
    /// </summary>
    public DateTime? ProgressResetAtUtc { get; set; }

    /// <summary>issue #361: üyeliği doğrulayan kullanıcının (admin/öğretmen) exam user id'si; migration geri doldurması için null.</summary>
    public int? SchoolVerifiedByUserId { get; set; }

    /// <summary>
    /// issue #361 review: son reddedilen okul üyeliği başvurusunun okulu. Ret anında <see cref="SchoolId"/> temizlenir; öğrenci
    /// aynı okulu <c>StudentService.SchoolRejectCooldown</c> (7 gün) dolmadan yeniden isteyemez (başka okul serbest).
    /// </summary>
    public int? LastRejectedSchoolId { get; set; }

    /// <summary>issue #361 review: son okul üyeliği reddinin anı (UTC).</summary>
    public DateTime? SchoolRejectedAt { get; set; }

    /// <summary>issue #361 review: son reddi veren kullanıcının (admin/öğretmen) exam user id'si.</summary>
    public int? SchoolRejectedByUserId { get; set; }

    /// <summary>
    /// issue #361: yetki kararlarında kullanılacak okul — yalnız doğrulanmış üyelikte <see cref="SchoolId"/>, aksi halde null.
    /// YALNIZ bellekteki entity için; LINQ-to-SQL sorgularında <c>s.SchoolVerifiedAt != null ? s.SchoolId : null</c> yazılmalı.
    /// </summary>
    [NotMapped]
    public int? VerifiedSchoolId => SchoolVerifiedAt.HasValue ? SchoolId : null;


    public int? GradeId { get; set; } // 🟢 Grade artık opsiyonel (nullable)

    [ForeignKey("GradeId")]
    public Grade? Grade { get; set; } // 🟢 Grade ilişkisi

    [MaxLength(20)]
    public string? ThemePreset { get; set; } = "standard"; // 🎨 Theme tercihi (minimal, standard, enhanced, full)

    public string? ThemeCustomConfig { get; set; } // 🎨 Custom theme config (JSON format)

    // public virtual ICollection<ExamResult> ExamResults { get; set; }

    public virtual ICollection<StudentPoint> StudentPoints { get; set; } = new List<StudentPoint>();
    public virtual ICollection<StudentBadge> StudentBadges { get; set; } = new List<StudentBadge>();
}

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

    [MaxLength(20)]
    public string? ThemePreset { get; set; } = "standard"; // 🎨 Theme tercihi (minimal, standard, enhanced, full)

    public string? ThemeCustomConfig { get; set; } // 🎨 Custom theme config (JSON format)



}

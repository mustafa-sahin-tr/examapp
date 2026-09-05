using System;

namespace ExamApp.Api.Data;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

    [MaxLength(20)]
    public string? ThemePreset { get; set; } = "standard"; // 🎨 Theme tercihi (minimal, standard, enhanced, full)

    public string? ThemeCustomConfig { get; set; } // 🎨 Custom theme config (JSON format)



}

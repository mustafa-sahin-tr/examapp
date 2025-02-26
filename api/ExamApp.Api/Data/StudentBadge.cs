using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class StudentBadge
{
    public int Id { get; set; }
    public int StudentId { get; set; } // Öğrenci FK
    public int BadgeId { get; set; } // Rozet FK
    public DateTime EarnedAt { get; set; } = DateTime.UtcNow; // Kazanıldığı tarih

[ForeignKey("StudentId")]
    public Student Student { get; set; }
[ForeignKey("BadgeId")]
    public Badge Badge { get; set; }
}


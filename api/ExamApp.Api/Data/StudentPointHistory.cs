using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Öğrencilerin hangi etkinliklerden puan kazandığını takip eder.
/// </summary>
public class StudentPointHistory
{
    public int Id { get; set; }

    public int StudentId { get; set; } // Öğrenci FK
    public int Points { get; set; } // Kazanılan puan
    public string Reason { get; set; } // "Test Tamamlama", "Günlük Giriş", "Özel Etkinlik" vb.
    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("StudentId")]
    public Student Student { get; set; }
}


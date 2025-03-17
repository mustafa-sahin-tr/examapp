using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Her hafta ve her ayın en iyi öğrencilerini sıralamak için oluşturulmuştur.
/// </summary>
public class Leaderboard : BaseEntity
{
    public int Id { get; set; }
    public int StudentId { get; set; } // Öğrenci FK
    public int TotalPoints { get; set; } // Toplam kazanılan puan
    public int Rank { get; set; } // Sıralama (1, 2, 3...)
    public string TimePeriod { get; set; } // "Weekly" veya "Monthly"
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("StudentId")]
    public Student Student { get; set; }
}


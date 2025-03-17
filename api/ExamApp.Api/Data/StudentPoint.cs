using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;

/// <summary>
/// Her öğrencinin toplam XP (deneyim puanı) ve seviyesini tutar.
/// </summary>
public class StudentPoint : BaseEntity
{
    public int Id { get; set; }
    public int StudentId { get; set; } // Öğrenci FK
    public int XP { get; set; } // Kazanılan toplam puan
    public int Level { get; set; } // Seviyesi
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    [ForeignKey("StudentId")]       
    public Student Student { get; set; }
}

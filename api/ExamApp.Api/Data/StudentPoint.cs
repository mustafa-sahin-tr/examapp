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

    /// <summary>
    /// Bu satıra son uygulanan BadgeService <c>StudentPointsChangedEvent.UpdatedAtUtc</c> değeri (issue #225).
    /// Versiyon görevi görür: yalnızca daha yeni event'ler uygulanır, eşit/eski olanlar no-op (idempotency +
    /// sırasız teslime karşı koruma). Null = satır henüz senkronla yazılmadı (eski/seed veri) → ilk event uygulanır.
    /// </summary>
    public DateTime? SourceUpdatedAtUtc { get; set; }

    [ForeignKey("StudentId")]       
    public Student Student { get; set; }
}

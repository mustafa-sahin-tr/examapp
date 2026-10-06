using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;


public enum WorksheetInstanceStatus
{
    Started = 0,   // 🟢 Test başladı
    Completed = 1, // ✅ Test tamamlandı
    Expired = 2    // ⏳ Süre doldu
}


public class WorksheetInstance : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int StudentId { get; set; }

    [ForeignKey("StudentId")]
    public Student Student { get; set; }

    [Required]
    public int WorksheetId { get; set; }

    [ForeignKey("WorksheetId")]
    public Worksheet Worksheet { get; set; }

    public DateTime StartTime { get; set; }

    /// <summary>
    /// issue #396: süre sınırının başlangıçtaki kopyası (<c>Worksheet.MaxDurationSeconds</c>); sunucu süre kontrolü buna
    /// bakar, öğretmen test süresini sonradan değiştirse de açık oturumun süresi değişmez. null/&lt;=0 = süre sınırı yok
    /// (mevcut satırlar migration ile worksheet değerinden doldurulur; yeni instance'ı start-test yazar).
    /// </summary>
    public int? MaxDurationSeconds { get; set; }
    public DateTime? EndTime { get; set; }

    public ICollection<WorksheetInstanceQuestion> WorksheetInstanceQuestions { get; set; }

    public WorksheetInstanceStatus Status { get; set; } // 🟢 Test durumu
}

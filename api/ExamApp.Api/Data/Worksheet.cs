using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;


public class Worksheet : BaseEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    [Required]
    public int GradeId { get; set; }

    [ForeignKey("GradeId")]
    public Grade Grade { get; set; }
    // Test içindeki sorular (Ara tablo ile ilişkilendirilecek)
    public int? SubjectId { get; set; }

    [ForeignKey("SubjectId")]
    public Subject Subject { get; set; }

    public int? TopicId { get; set; } // Konu ID'si (isteğe bağlı)
    [ForeignKey("TopicId")]
    public Topic? Topic { get; set; } // Konu (isteğe bağlı)

    public int? SubTopicId { get; set; } // Alt konu ID'si (isteğe bağlı)
    [ForeignKey("SubTopicId")]
    public SubTopic? SubTopic { get; set; } // Alt konu (isteğe bağlı)
    // Test içindeki sorular (Ara tablo ile ilişkilendirilecek)
    public ICollection<WorksheetQuestion> WorksheetQuestions { get; set; } = new List<WorksheetQuestion>();
    public int MaxDurationSeconds { get; set; } // 🕒 Maksimum test süresi (saniye)
    public bool IsPracticeTest { get; set; } // True ise Çalışma Testi, False ise Normal Test

    public string? Subtitle { get; set; }

    public string? ImageUrl { get; set; }

    public string? BadgeText { get; set; }

    public int? BookTestId { get; set; }  // Kitap-Test ilişkisi için
    
    [ForeignKey("BookTestId")]
    public BookTest? BookTest { get; set; }  // Navigation Property

}

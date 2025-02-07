using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;

public class Test
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
    // Test içindeki sorular (Ara tablo ile ilişkilendirilecek)
    public ICollection<TestQuestion> TestQuestions { get; set; } = new List<TestQuestion>();
}

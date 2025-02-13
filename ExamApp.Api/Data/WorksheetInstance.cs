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


public class WorksheetInstance
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
    public DateTime? EndTime { get; set; }

    public ICollection<WorksheetInstanceQuestion> WorksheetInstanceQuestions { get; set; }

    public WorksheetInstanceStatus Status { get; set; } // 🟢 Test durumu
}

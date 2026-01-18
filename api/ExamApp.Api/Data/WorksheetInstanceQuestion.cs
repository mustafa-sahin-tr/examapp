using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;

public class WorksheetInstanceQuestion : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int WorksheetInstanceId { get; set; }

    [ForeignKey("WorksheetInstanceId")]
    public WorksheetInstance WorksheetInstance { get; set; }

    [Required]
    public int WorksheetQuestionId { get; set; }

    [ForeignKey("WorksheetQuestionId")]
    public WorksheetQuestion WorksheetQuestion { get; set; }

    public int? SelectedAnswerId { get; set; }

    // Non-MCQ answers (e.g. drag-drop) stored as JSON payload
    public string? AnswerPayload { get; set; }

    [ForeignKey("SelectedAnswerId")]
    public Answer SelectedAnswer { get; set; }

    public bool IsCorrect { get; set; }
    public int TimeTaken { get; set; } // Kaç saniyede çözüldü
    public bool ShowCorrectAnswer { get; set; } // Kullanıcı "Sonucu Gör" yaptı mı?

}

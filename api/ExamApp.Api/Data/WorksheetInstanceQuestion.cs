using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;
using ExamApp.Foundation.Contracts;

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

    /// <summary>
    /// issue #279 review (blocker + security M1/L3): DB-generated monoton revizyon — her
    /// <c>TestSessionService.SaveAnswer</c> çağrısında atomik olarak artırılır (bkz. o metodun XML doc'u)
    /// ve <see cref="AnswerSubmittedEvent"/> ile taşınır. BadgeService'te
    /// <c>AnswerPointAward</c>'ın revizyon karşılaştırmasının birincil kaynağı budur — istemci saatine
    /// bağlı olmayan, DB tarafında UPDATE sırasına göre kesin sıralı bir sayaçtır (eski
    /// <c>AnswerSubmittedEvent.SubmittedAt</c> mikrosaniye altı çakışmalarda/istemci saat kaymasında
    /// sıra karıştırabiliyordu). Varsayılan 0 = henüz hiç cevaplanmamış satır.
    /// </summary>
    public int AnswerRevision { get; set; }
}

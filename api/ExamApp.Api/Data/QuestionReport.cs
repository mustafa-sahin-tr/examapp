using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>Soru bildirim nedeni (issue #781). DB'de string.</summary>
public enum QuestionReportReason
{
    WrongAnswer = 1,
    Unreadable = 2,
    WrongContent = 3,
    Other = 4
}

public enum QuestionReportStatus
{
    Open = 0,
    Reviewed = 1
}

/// <summary>
/// issue #781: öğrencinin çözdüğü testteki bir soruyu bildirmesi. Soru sahibi ve admin görür; bildireni görmezler.
/// Aynı öğrenci aynı soruyu bir kez bildirir, tekrar istek mevcut kaydı döner.
/// </summary>
public class QuestionReport : BaseEntity
{
    public const int NoteMaxLength = 500;

    [Key]
    public int Id { get; set; }

    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public Question Question { get; set; } = null!;

    public int ReporterUserId { get; set; }

    /// <summary>Bildirimin yapıldığı test oturumu.</summary>
    public int TestInstanceId { get; set; }

    public QuestionReportReason Reason { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    public QuestionReportStatus Status { get; set; } = QuestionReportStatus.Open;
}

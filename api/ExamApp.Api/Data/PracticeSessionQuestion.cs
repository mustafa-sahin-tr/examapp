using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Pratik oturumunda öğrenciye gösterilen bir soru ve (varsa) verdiği cevap.
/// Satır, soru gösterildiği anda açılır (<see cref="ShownAt"/>); cevap/pas geldiğinde güncellenir.
/// Böylece cevaplanmamış bir soru bile aynı oturumda tekrar gösterilmez.
/// Alan adları (<see cref="IsCorrect"/>, <see cref="TimeTaken"/>, <see cref="SelectedAnswerId"/>)
/// ileride ortak raporlama için <see cref="WorksheetInstanceQuestion"/> ile aynı tutulmuştur.
/// </summary>
public class PracticeSessionQuestion : BaseEntity
{
    public int Id { get; set; }

    public int PracticeSessionId { get; set; }

    [ForeignKey(nameof(PracticeSessionId))]
    public PracticeSession PracticeSession { get; set; } = default!;

    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public Question Question { get; set; } = default!;

    public DateTime ShownAt { get; set; }
    public DateTime? AnsweredAt { get; set; }

    public int? SelectedAnswerId { get; set; }

    [ForeignKey(nameof(SelectedAnswerId))]
    public Answer? SelectedAnswer { get; set; }

    public bool IsCorrect { get; set; }

    /// <summary>Öğrenci soruyu cevaplamadan geçti.</summary>
    public bool IsSkipped { get; set; }

    /// <summary>Saniye.</summary>
    public int TimeTaken { get; set; }
}

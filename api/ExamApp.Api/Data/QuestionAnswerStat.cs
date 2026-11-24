using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #526 (<c>global</c> hard questions): how many distinct students answered a question and how many of them got it right,
/// precomputed nightly by <see cref="Services.Practice.QuestionAnswerStatsJob"/> (no aggregate query at request time). One row
/// per question with at least one answer.
/// </summary>
public class QuestionAnswerStat
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public Question Question { get; set; } = default!;

    /// <summary>Distinct students with an answer (a blank answer is not one).</summary>
    public int StudentCount { get; set; }

    /// <summary>Students whose first answer was correct.</summary>
    public int CorrectCount { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}

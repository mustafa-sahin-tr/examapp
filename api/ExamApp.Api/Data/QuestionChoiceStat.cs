using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #528: how many distinct students picked a choice of a question with their first answer, precomputed nightly with
/// <see cref="QuestionAnswerStat"/> by <see cref="Services.Practice.QuestionAnswerStatsJob"/>. Gives each question's "popular
/// distractor" (the wrong choice most students picked) as an aggregate only; no student's own choice is stored here.
/// </summary>
public class QuestionChoiceStat
{
    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public Question Question { get; set; } = default!;

    public int AnswerId { get; set; }

    /// <summary>Students whose first answer was this choice.</summary>
    public int StudentCount { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}

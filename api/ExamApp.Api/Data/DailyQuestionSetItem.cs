using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// <see cref="DailyQuestionSet"/>'in bir sorusu (issue #99). <see cref="Order"/> 0 tabanlı ve set içinde tekildir; aynı soru
/// set içinde bir kez bulunur (ikisi de unique index ile DB seviyesinde).
/// </summary>
public class DailyQuestionSetItem : BaseEntity
{
    public int Id { get; set; }

    public int DailyQuestionSetId { get; set; }

    [ForeignKey(nameof(DailyQuestionSetId))]
    public DailyQuestionSet DailyQuestionSet { get; set; } = default!;

    public int QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    public Question Question { get; set; } = default!;

    /// <summary>0 tabanlı çözüm sırası.</summary>
    public int Order { get; set; }
}

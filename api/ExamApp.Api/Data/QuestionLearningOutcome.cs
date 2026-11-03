namespace ExamApp.Api.Data;

/// <summary>Issue #692: soru ile MEB kazanımı arasındaki çoka çok bağ.</summary>
public class QuestionLearningOutcome : BaseEntity
{
    public int Id { get; set; }

    public int QuestionId { get; set; }
    public Question Question { get; set; } = default!;

    public int LearningOutcomeId { get; set; }
    public LearningOutcome LearningOutcome { get; set; } = default!;
}

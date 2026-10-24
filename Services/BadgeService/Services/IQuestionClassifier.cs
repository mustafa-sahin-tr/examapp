namespace BadgeService.Services;

/// <summary>
/// Classifies a question (subject / topic / subtopic / difficulty) from its image using an LLM,
/// then writes the result back to the exam API. Replaces the former n8n "analyze-question" webhook.
/// <paramref name="gradeId"/> (issue #565) restricts the taxonomy to one grade; <see langword="null"/> = whole taxonomy.
/// </summary>
public interface IQuestionClassifier
{
    Task ClassifyAndPersistAsync(int questionId, int? gradeId, CancellationToken cancellationToken);
}

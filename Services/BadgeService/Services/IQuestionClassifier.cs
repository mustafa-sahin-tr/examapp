namespace BadgeService.Services;

/// <summary>
/// Classifies a question (subject / topic / subtopic / difficulty) from its image using an LLM,
/// then writes the result back to the exam API. Replaces the former n8n "analyze-question" webhook.
/// </summary>
public interface IQuestionClassifier
{
    Task ClassifyAndPersistAsync(int questionId, CancellationToken cancellationToken);
}

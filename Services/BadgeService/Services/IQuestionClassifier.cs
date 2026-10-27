namespace BadgeService.Services;

/// <summary>
/// Classifies a question (subject / topic / subtopic / difficulty) from its image using an LLM,
/// then writes the result back to the exam API. Replaces the former n8n "analyze-question" webhook.
/// <paramref name="gradeId"/> (issue #565) restricts the taxonomy to one grade; <see langword="null"/> = whole taxonomy.
/// </summary>
public interface IQuestionClassifier
{
    Task ClassifyAndPersistAsync(int questionId, int? gradeId, CancellationToken cancellationToken);

    /// <summary>
    /// Issue #577 (book TOC, <see cref="BookTocMode.Hybrid"/>): the topic is known — classify only the subtopic and
    /// difficulty within <paramref name="topicId"/>; topic and subject are not changed and the persisted source stays
    /// <c>Book</c>. Providers that cannot constrain the taxonomy skip (the book's topic is kept).
    /// </summary>
    Task ClassifyWithinTopicAndPersistAsync(int questionId, int? gradeId, int topicId, CancellationToken cancellationToken);
}

namespace BadgeService.Services;

/// <summary>
/// Classifies a question (subject / topic / subtopic / difficulty) from its image using an LLM — or, for a question
/// without an image (issue #590, text-authored activity / text MCQ), from its text where the provider supports it —
/// then writes the result back to the exam API. Replaces the former n8n "analyze-question" webhook.
/// <paramref name="gradeId"/> (issue #565) restricts the taxonomy to one grade; <see langword="null"/> = whole taxonomy.
/// </summary>
public interface IQuestionClassifier
{
    Task ClassifyAndPersistAsync(int questionId, int? gradeId, CancellationToken cancellationToken);

    /// <summary>
    /// Issue #577 (book TOC, <see cref="BookTocMode.Hybrid"/>): the topic is known — classify only the subtopic and
    /// difficulty within <paramref name="topicId"/>; topic and subject are not changed and the persisted source is
    /// <paramref name="classificationSource"/> (<c>Book</c>, or <c>Human</c> for a teacher-locked topic, issue #590). Providers
    /// that cannot constrain the taxonomy skip (the given topic is kept).
    /// </summary>
    Task ClassifyWithinTopicAndPersistAsync(
        int questionId, int? gradeId, int topicId, string classificationSource, CancellationToken cancellationToken);
}

using System.Text.Json;
using BadgeService.Models;

namespace BadgeService.Services;

/// <summary>
/// The subject → topic → subtopic taxonomy as the exam API serves it (issue #469). <see cref="Json"/> is kept
/// verbatim — it goes into the prompt-cached system message, so its bytes must not change between calls;
/// the parsed id maps are used to reject a classification that names an id outside the taxonomy.
/// </summary>
public sealed class ClassifierTaxonomy
{
    private readonly HashSet<int> _subjects;
    private readonly HashSet<int> _topics;
    private readonly HashSet<int> _subTopics;

    private ClassifierTaxonomy(string json, string hash, HashSet<int> subjects, HashSet<int> topics, HashSet<int> subTopics)
    {
        Json = json;
        Hash = hash;
        _subjects = subjects;
        _topics = topics;
        _subTopics = subTopics;
    }

    public string Json { get; }

    public string Hash { get; }

    public int SubTopicCount => _subTopics.Count;

    /// <summary>Parses <c>{ subjects: [ { id, topics: [ { id, subTopics: [ { id } ] } ] } ] }</c>.</summary>
    public static ClassifierTaxonomy Parse(string json, string hash)
    {
        var subjects = new HashSet<int>();
        var topics = new HashSet<int>();
        var subTopics = new HashSet<int>();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("subjects", out var subjectArray))
        {
            foreach (var subject in subjectArray.EnumerateArray())
            {
                subjects.Add(subject.GetProperty("id").GetInt32());
                if (!subject.TryGetProperty("topics", out var topicArray))
                    continue;

                foreach (var topic in topicArray.EnumerateArray())
                {
                    topics.Add(topic.GetProperty("id").GetInt32());
                    if (!topic.TryGetProperty("subTopics", out var subTopicArray))
                        continue;

                    foreach (var subTopic in subTopicArray.EnumerateArray())
                        subTopics.Add(subTopic.GetProperty("id").GetInt32());
                }
            }
        }

        return new ClassifierTaxonomy(json, hash, subjects, topics, subTopics);
    }

    /// <summary>
    /// The first id in <paramref name="result"/> that is not in the taxonomy, as a log-friendly message, or
    /// <see langword="null"/> when every id is known. Hierarchy is not checked: like a human edit, the exam API
    /// accepts subtopics from several topics and derives subject/topic from the first subtopic.
    /// </summary>
    public string? FindUnknownId(QuestionClassificationResult result)
    {
        if (result.SubjectId is { } subjectId && !_subjects.Contains(subjectId))
            return $"subjectId {subjectId}";
        if (result.TopicId is { } topicId && !_topics.Contains(topicId))
            return $"topicId {topicId}";
        foreach (var subTopicId in result.SubTopicIds)
        {
            if (!_subTopics.Contains(subTopicId))
                return $"subTopicId {subTopicId}";
        }
        return null;
    }
}

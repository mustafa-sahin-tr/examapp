using System.Text.Json.Serialization;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

[JsonConverter(typeof(JsonStringEnumConverter<LearningOutcomeMasteryLevel>))]
public enum LearningOutcomeMasteryLevel
{
    [JsonStringEnumMemberName("notStarted")]
    NotStarted,
    [JsonStringEnumMemberName("familiar")]
    Familiar,
    [JsonStringEnumMemberName("proficient")]
    Proficient,
    [JsonStringEnumMemberName("mastered")]
    Mastered,
}

/// <summary>Satır neyi ölçüyor: kazanım; kazanım eşlenmemiş soruda alt konu ya da konu.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MasteryItemKind>))]
public enum MasteryItemKind
{
    [JsonStringEnumMemberName("outcome")]
    Outcome,
    [JsonStringEnumMemberName("subTopic")]
    SubTopic,
    [JsonStringEnumMemberName("topic")]
    Topic,
}

/// <summary>Issue #696: bir kazanımın (ya da yedek olarak alt konu, konunun) seviyesi. Sayılar son cevaplardandır, tüm geçmişten değil.</summary>
public class LearningOutcomeMasteryItemDto
{
    public MasteryItemKind Kind { get; set; }

    public int? LearningOutcomeId { get; set; }
    public string? Code { get; set; }

    /// <summary>Kazanımın resmî ifadesi; soru içeriği değildir.</summary>
    public string? Statement { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<CurriculumVersion>))]
    public CurriculumVersion? CurriculumVersion { get; set; }

    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int? TopicId { get; set; }
    public string? TopicName { get; set; }
    public int? SubTopicId { get; set; }
    public string? SubTopicName { get; set; }

    public LearningOutcomeMasteryLevel Level { get; set; }

    /// <summary>Seviye az cevaba dayanıyor: kesin gösterilmez.</summary>
    public bool IsTentative { get; set; }

    public int Answered { get; set; }
    public int Correct { get; set; }

    /// <summary>Cevap yoksa boş.</summary>
    public decimal? AccuracyPct { get; set; }
}

/// <summary>Seviye kuralları sunucudan gelir: ekrandaki açıklama ayarla aynı kalır.</summary>
public class LearningOutcomeMasteryRulesDto
{
    public int RecentAnswers { get; set; }
    public int ProficientPct { get; set; }
    public int MasteredPct { get; set; }
    public int MasteredMinAnswers { get; set; }
    public int ReliableMinAnswers { get; set; }
}

public class LearningOutcomeMasteryDto
{
    public DateTime To { get; set; }

    /// <summary>Ders, konu, alt konu, koda göre sıralı.</summary>
    public List<LearningOutcomeMasteryItemDto> Items { get; set; } = [];

    /// <summary>Cevaplanan ama kazanımı, alt konusu ve konusu olmayan farklı soru sayısı.</summary>
    public int UnclassifiedCount { get; set; }

    public LearningOutcomeMasteryRulesDto Rules { get; set; } = new();
}

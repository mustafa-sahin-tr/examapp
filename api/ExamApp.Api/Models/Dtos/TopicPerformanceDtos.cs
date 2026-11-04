using System.Text.Json.Serialization;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Yanıtta da istekteki adlarla döner.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TopicPerformanceGroupBy>))]
public enum TopicPerformanceGroupBy
{
    [JsonStringEnumMemberName("topic")]
    Topic,
    [JsonStringEnumMemberName("subtopic")]
    SubTopic,
    [JsonStringEnumMemberName("difficulty")]
    Difficulty,
    [JsonStringEnumMemberName("interactionType")]
    InteractionType,
}

/// <summary>Issue #497: bir grubun doğru, yanlış, boş sayıları. Yüzdeler sunucuda hesaplanır, istemci yeniden üretmez.</summary>
public class TopicPerformanceItemDto
{
    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int? TopicId { get; set; }
    public string? TopicName { get; set; }
    public int? SubTopicId { get; set; }
    public string? SubTopicName { get; set; }

    /// <summary>easy, medium, hard (<see cref="Helpers.DifficultyBands"/>). Yalnız zorluğa göre gruplarken dolu.</summary>
    public string? DifficultyBand { get; set; }

    /// <summary>Yalnız soru tipine göre gruplarken dolu; etkileşim tipi olmayan soru mcq'dur.</summary>
    public string? InteractionType { get; set; }

    public int Correct { get; set; }
    public int Wrong { get; set; }
    public int Blank { get; set; }
    public int Total { get; set; }

    /// <summary>Doğru / (doğru + yanlış). Boşlar girmez: terk edilmiş test konuyu zayıf göstermez.</summary>
    public decimal AccuracyPct { get; set; }

    /// <summary>Cevaplanan satırların ortalama süresi; boş satır ortalamayı düşürmez.</summary>
    public decimal? AvgTimeSec { get; set; }

    /// <summary>Aynı uzunluktaki önceki pencerede cevaplanan soru sayısı: az soruya dayanan eğilim buradan anlaşılır.</summary>
    public int PrevAnswered { get; set; }
    public decimal? PrevAccuracyPct { get; set; }

    /// <summary>Doğruluk farkı (yüzde puanı); önceki pencerede cevap yoksa boş.</summary>
    public decimal? Trend { get; set; }
}

public class TopicPerformanceDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public TopicPerformanceGroupBy GroupBy { get; set; }

    /// <summary>En düşük doğruluk önce.</summary>
    public List<TopicPerformanceItemDto> Items { get; set; } = [];

    /// <summary>Pencerede cevaplanan ama konusu olmayan farklı soru sayısı; konuya göre gruplarken listeye girmez.</summary>
    public int UnclassifiedCount { get; set; }
}

using ExamApp.Api.Services.Storage;

namespace ExamApp.Api.Services.Placement;

public sealed class PlacementStatusDto
{
    /// <summary>Öğrencinin sınıfında yayımlanmış test var.</summary>
    public bool Available { get; set; }
    public bool Taken { get; set; }
    public string? GradeName { get; set; }
    public int QuestionCount { get; set; }
}

public sealed class PlacementAnswerOptionDto
{
    public int Id { get; set; }
    public string? Text { get; set; }

    [StorageUrl(StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }
}

/// <summary>Öğrenciye giden soru: doğru şık yok.</summary>
public sealed class PlacementQuestionDto
{
    public int Id { get; set; }
    public string? SubjectName { get; set; }
    public string? Text { get; set; }

    [StorageUrl(StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }

    public List<PlacementAnswerOptionDto> Options { get; set; } = new();
}

public sealed class PlacementAnswerDto
{
    public int QuestionId { get; set; }
    /// <summary>Boş bırakılan soru null.</summary>
    public int? AnswerId { get; set; }
}

public sealed class PlacementSubmitDto
{
    public List<PlacementAnswerDto> Answers { get; set; } = new();
}

public sealed class PlacementSubjectScoreDto
{
    public int SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public int Correct { get; set; }
    public int Total { get; set; }
    public int Percent { get; set; }
}

public sealed class PlacementTopicDto
{
    public int TopicId { get; set; }
    public string TopicName { get; set; } = string.Empty;
    public string? SubjectName { get; set; }
    public int Correct { get; set; }
    public int Total { get; set; }
}

/// <summary>
/// Plan önerisi: <c>Tier</c> basic / essential / pro (<c>PlanKey</c> o kademenin öğrenci planı); <c>SubjectId</c> doluysa tek ders
/// kapsamı (#1109), boşsa bütün dersler. <c>Band</c> öğrencinin sınıf bandı (fiyat).
/// </summary>
public sealed class PlacementSuggestionDto
{
    public string Tier { get; set; } = string.Empty;
    public string PlanKey { get; set; } = string.Empty;
    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public string? Band { get; set; }
}

public sealed class PlacementReportDto
{
    public string? GradeName { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public int Correct { get; set; }
    public int Total { get; set; }
    public int Percent { get; set; }
    public List<PlacementSubjectScoreDto> Subjects { get; set; } = new();
    public List<PlacementTopicDto> StrongTopics { get; set; } = new();
    public List<PlacementTopicDto> WeakTopics { get; set; } = new();
    public PlacementSuggestionDto Suggestion { get; set; } = new();
}

public sealed class AdminPlacementTestDto
{
    public int GradeId { get; set; }
    public string GradeName { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    /// <summary>Ders başına soru sayısı (yalnız tek testi okurken dolu).</summary>
    public List<PlacementSubjectScoreDto> Subjects { get; set; } = new();
}

public class AdminPlacementPublishDto
{
    public bool Published { get; set; }
}

/// <summary>Veli: çocuğun raporu; çözülmemişse null.</summary>
public sealed class ParentPlacementDto
{
    public PlacementReportDto? Report { get; set; }
}

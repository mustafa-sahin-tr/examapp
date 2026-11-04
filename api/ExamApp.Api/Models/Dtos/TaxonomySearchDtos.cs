namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #499: aramanın bir sonucu. <see cref="TopicId"/> ve <see cref="SubTopicId"/> boşsa sonuç dersin kendisidir.</summary>
public class TaxonomySearchResultDto
{
    public int SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public int GradeId { get; set; }
    public string GradeName { get; set; } = string.Empty;

    /// <summary>Örn. "Matematik › Açıların Ölçüsü".</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Yalnız sıralama içindir, sürümler arasında karşılaştırılmaz.</summary>
    public int Score { get; set; }
}

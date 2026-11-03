using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #690: GET api/admin/question-sources satırı.</summary>
public class QuestionSourceItemDto
{
    public int QuestionId { get; set; }
    public string? Text { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? RightsHolder { get; set; }
    public string? LicenseReference { get; set; }
    public DateTime? LicenseEndsAt { get; set; }
    public bool IsUnpublished { get; set; }
}

/// <summary>Seçilen sorulara kaynak bilgisi yazar.</summary>
public class QuestionSourceAssignDto
{
    [Required, MinLength(1), MaxLength(500)]
    public List<int> QuestionIds { get; set; } = new();

    [Required]
    public string SourceType { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? RightsHolder { get; set; }

    [MaxLength(200)]
    public string? LicenseReference { get; set; }

    public DateTime? LicenseEndsAt { get; set; }
}

/// <summary>Bir hak sahibinin bütün sorularını yayından kaldırır ya da geri alır.</summary>
public class QuestionSourcePublishDto
{
    [Required, MaxLength(200)]
    public string RightsHolder { get; set; } = string.Empty;

    public bool Unpublished { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}

public class QuestionSourceActionResultDto
{
    public int AffectedCount { get; set; }
}

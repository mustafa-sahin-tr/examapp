using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

public class GenerateQuestionDraftsDto
{
    public int LearningOutcomeId { get; set; }

    [Range(1, 5)]
    public int Count { get; set; } = 3;

    /// <summary>Sorunun zorluk ölçeği 1-10.</summary>
    [Range(1, 10)]
    public int DifficultyLevel { get; set; } = 5;
}

public class QuestionDraftDto
{
    [MaxLength(4000)]
    public string Text { get; set; } = string.Empty;

    [MaxLength(10)]
    public List<string> Options { get; set; } = [];

    public int CorrectIndex { get; set; }

    /// <summary>İnceleyen için doğru cevabın gerekçesi; soruyla kaydedilmez.</summary>
    [MaxLength(4000)]
    public string? Explanation { get; set; }
}

public class QuestionDraftsResponseDto
{
    public int LearningOutcomeId { get; set; }
    public string OutcomeCode { get; set; } = string.Empty;
    public string OutcomeDescription { get; set; } = string.Empty;
    public List<QuestionDraftDto> Drafts { get; set; } = [];

    /// <summary>Kurallara uymadığı için elenen taslak sayısı (eksik şık, görsele atıf gibi).</summary>
    public int Rejected { get; set; }
}

public class SaveQuestionDraftsDto
{
    public int LearningOutcomeId { get; set; }

    [Range(1, 10)]
    public int DifficultyLevel { get; set; } = 5;

    [Required]
    [MinLength(1)]
    [MaxLength(20)]
    public List<QuestionDraftDto> Questions { get; set; } = [];
}

public class SavedQuestionDraftsDto
{
    public List<int> QuestionIds { get; set; } = [];
}

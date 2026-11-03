using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #692: öğretmenin seçim listesindeki ve sorunun bağlı kazanımı.</summary>
public class LearningOutcomeDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CurriculumVersion { get; set; } = string.Empty;
    public int GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? SubTopicId { get; set; }
}

/// <summary>PUT api/questions/{id}/learning-outcomes: sorunun bağlı kazanımlarının tamamı; boş liste bağları kaldırır.</summary>
public class SetQuestionLearningOutcomesDto
{
    [Required, MaxLength(20)]
    public List<int> LearningOutcomeIds { get; set; } = new();
}

/// <summary>POST api/admin/learning-outcomes/import: tek müfredatın, resmî belgeden çıkarılmış listesi.</summary>
public class LearningOutcomeImportDto
{
    [Required]
    public string CurriculumVersion { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? SourceReference { get; set; }

    [Required, MinLength(1), MaxLength(5000)]
    public List<LearningOutcomeImportItemDto> Items { get; set; } = new();
}

/// <summary>Sınıf ve ders ada göre eşleşir: liste ortamlar arasında aynı dosyayla taşınır.</summary>
public class LearningOutcomeImportItemDto
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string GradeName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
}

/// <summary>Hatalı satır varsa hiçbir şey yazılmaz: <see cref="Errors"/> dolar, sayılar 0 kalır.</summary>
public class LearningOutcomeImportResultDto
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public List<string> Errors { get; set; } = new();
}

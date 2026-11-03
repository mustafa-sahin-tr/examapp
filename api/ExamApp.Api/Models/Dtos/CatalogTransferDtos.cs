namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Sınıf, ders, konu, alt konu ve kazanım kataloğunun ortamlar arasında taşınan dosyası.
/// Veritabanı kimliği taşımaz: kayıtlar koda göre (kazanım müfredat + koda göre) eşleşir.
/// </summary>
public class CatalogBundleDto
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public List<CatalogNamedDto> Grades { get; set; } = new();
    public List<CatalogNamedDto> Subjects { get; set; } = new();
    public List<CatalogGradeSubjectDto> GradeSubjects { get; set; } = new();
    public List<CatalogTopicDto> Topics { get; set; } = new();
    public List<CatalogLearningOutcomeDto> LearningOutcomes { get; set; } = new();
}

public class CatalogNamedDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Sınıf ve ders kodlarıyla bağ.</summary>
public class CatalogGradeSubjectDto
{
    public string Grade { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
}

public class CatalogTopicDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public List<CatalogNamedDto> SubTopics { get; set; } = new();
}

public class CatalogLearningOutcomeDto
{
    public string CurriculumVersion { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? SubTopic { get; set; }
    public string? SourceReference { get; set; }
}

public class CatalogImportCountsDto
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
}

public class CatalogImportResultDto
{
    public bool DryRun { get; set; }
    public CatalogImportCountsDto Grades { get; set; } = new();
    public CatalogImportCountsDto Subjects { get; set; } = new();
    public CatalogImportCountsDto GradeSubjects { get; set; } = new();
    public CatalogImportCountsDto Topics { get; set; } = new();
    public CatalogImportCountsDto SubTopics { get; set; } = new();
    public CatalogImportCountsDto LearningOutcomes { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

using ExamApp.Api.Services.Storage;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Models.Dtos;

public class ExamTemplateSectionDto
{
    public int Id { get; set; }

    [MaxLength(ExamTemplate.NameMaxLength)]
    public string? GroupName { get; set; }

    [Required, MaxLength(ExamTemplate.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Range(1, 200)]
    public int QuestionCount { get; set; }
}

public class ExamTemplateSessionDto
{
    public int Id { get; set; }

    [Required, MaxLength(ExamTemplate.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Range(60, WorksheetDurationLimits.MaxSeconds)]
    public int DurationSeconds { get; set; }

    [Required, MinLength(1), MaxLength(30)]
    public List<ExamTemplateSectionDto> Sections { get; set; } = [];
}

public class ExamTemplateDto
{
    public int Id { get; set; }

    [Required, MaxLength(ExamTemplate.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(ExamTemplate.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 10)]
    public int WrongAnswersPerCorrect { get; set; }

    [MaxLength(300)]
    public string? SourceReference { get; set; }

    [Required, MinLength(1), MaxLength(10)]
    public List<ExamTemplateSessionDto> Sessions { get; set; } = [];

    /// <summary>Bu şablondan deneme kuruldu: yapı değişirse sonuçlar bozulur, değiştirilemez.</summary>
    public bool InUse { get; set; }

    /// <summary>Bu şablondan kurulmuş deneme sayısı (#899); yalnız yanıtta dolar.</summary>
    public int MockExamCount { get; set; }
}

public class CreateMockExamDto
{
    public int ExamTemplateId { get; set; }

    [Required, MaxLength(150), PlainText]
    public string Name { get; set; } = string.Empty;

    public int GradeId { get; set; }
}

public class SetSectionQuestionsDto
{
    [Required, MaxLength(200)]
    public List<int> QuestionIds { get; set; } = [];
}

public class PublishMockExamDto
{
    /// <summary>true: bu sınıftaki bütün öğrenciler (bütün okullar) başlatır. false: yalnız atanan öğrenciler. Varsayılan kapalıdır.</summary>
    public bool OpenToGrade { get; set; }
}

/// <summary>Kurulum ekranında soruyu ayırt etmeye yeter: görsel ya da metin.</summary>
public class MockExamQuestionCardDto
{
    public int Id { get; set; }
    public string? Text { get; set; }

    [StorageUrl(StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }
}

public class MockExamSectionDetailDto
{
    public int SectionId { get; set; }
    public string? GroupName { get; set; }
    public string Name { get; set; } = string.Empty;
    public int RequiredCount { get; set; }
    public List<int> QuestionIds { get; set; } = [];
    public List<MockExamQuestionCardDto> Questions { get; set; } = [];
}

public class MockExamSessionDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public int WorksheetId { get; set; }
    public List<MockExamSectionDetailDto> Sections { get; set; } = [];
}

public class MockExamDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ExamTemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public int GradeId { get; set; }
    public string GradeName { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    public List<MockExamSessionDetailDto> Sessions { get; set; } = [];

    /// <summary>Yayını engelleyen sorunlar.</summary>
    public List<MockExamProblemDto> Problems { get; set; } = [];
}

/// <summary><see cref="Code"/> ekranda çevrilir: WrongCount, NoCorrectAnswer, Unpublished, LicenseEnded.</summary>
public class MockExamProblemDto
{
    public string Code { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public int? QuestionId { get; set; }
    public int? Required { get; set; }
    public int? Actual { get; set; }
}

public class MockExamListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string GradeName { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
}

public class StudentMockExamSessionDto
{
    public string Name { get; set; } = string.Empty;
    public int WorksheetId { get; set; }
    public int DurationSeconds { get; set; }
    public int QuestionCount { get; set; }

    /// <summary>NotStarted, InProgress, Finished.</summary>
    public string Status { get; set; } = string.Empty;
    public int? InstanceId { get; set; }

    /// <summary>Atama yalnız bir oturumu kapsıyorsa diğerleri başlatılamaz.</summary>
    public bool CanStart { get; set; }
}

public class StudentMockExamDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public List<StudentMockExamSessionDto> Sessions { get; set; } = [];
}

public class MockExamScoreDto
{
    public string? GroupName { get; set; }
    public string Name { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public int Correct { get; set; }
    public int Wrong { get; set; }
    public int Blank { get; set; }
    public decimal Net { get; set; }

    /// <summary>Oturumu bitmemiş ders: sayılar yok.</summary>
    public bool Pending { get; set; }
}

public class MockExamResultDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public int WrongAnswersPerCorrect { get; set; }
    public bool Complete { get; set; }
    public List<MockExamScoreDto> Sections { get; set; } = [];
    public int Correct { get; set; }
    public int Wrong { get; set; }
    public int Blank { get; set; }
    public decimal Net { get; set; }
}

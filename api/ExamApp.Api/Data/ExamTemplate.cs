using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #693: sınav kuralı koddan değil veriden gelir (LGS, TYT, AYT aynı yapıyla). Oturum süresi ve soru sayıları
/// <see cref="ExamTemplateSeed"/> içindeki resmî kılavuzlardan alınmıştır; kılavuz değişince şablon güncellenir.
/// </summary>
public class ExamTemplate : BaseEntity
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 100;

    public int Id { get; set; }

    [MaxLength(CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Kaç yanlış bir doğruyu götürür: LGS 3, YKS 4; 0 yanlış cezası yok.</summary>
    public int WrongAnswersPerCorrect { get; set; }

    [MaxLength(300)]
    public string? SourceReference { get; set; }

    public ICollection<ExamTemplateSession> Sessions { get; set; } = new List<ExamTemplateSession>();
}

public class ExamTemplateSession : BaseEntity
{
    public int Id { get; set; }
    public int ExamTemplateId { get; set; }
    public ExamTemplate ExamTemplate { get; set; } = null!;
    public int Order { get; set; }

    [MaxLength(ExamTemplate.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public int DurationSeconds { get; set; }

    public ICollection<ExamTemplateSection> Sections { get; set; } = new List<ExamTemplateSection>();
}

/// <summary>Net hesabının yapıldığı ders satırı. <see cref="GroupName"/> sınavdaki testtir: TYT Sosyal Bilimler altında Tarih.</summary>
public class ExamTemplateSection : BaseEntity
{
    public int Id { get; set; }
    public int ExamTemplateSessionId { get; set; }
    public ExamTemplateSession Session { get; set; } = null!;
    public int Order { get; set; }

    [MaxLength(ExamTemplate.NameMaxLength)]
    public string? GroupName { get; set; }

    [MaxLength(ExamTemplate.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public int QuestionCount { get; set; }
}

/// <summary>Şablondan kurulan deneme. Her oturum sıradan bir test kaydıdır: süre sınırı, oturum ve puanlama aynı yoldan geçer.</summary>
public class MockExam : BaseEntity
{
    public int Id { get; set; }
    public int ExamTemplateId { get; set; }
    public ExamTemplate ExamTemplate { get; set; } = null!;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public int GradeId { get; set; }
    public Grade Grade { get; set; } = null!;

    /// <summary>Boşsa taslaktır: öğrenci görmez, sorular değişebilir.</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// Sınıftaki öğrenciler atama olmadan başlatabilir. Oturum kayıtları her durumda <see cref="WorksheetStudentVisibility.Restricted"/> kalır:
    /// normal test listesine ve pratik havuzuna girmez, sınav soruları açığa çıkmaz.
    /// </summary>
    public bool OpenToGrade { get; set; }

    public ICollection<MockExamSession> Sessions { get; set; } = new List<MockExamSession>();
}

public class MockExamSession : BaseEntity
{
    public int Id { get; set; }
    public int MockExamId { get; set; }
    public MockExam MockExam { get; set; } = null!;
    public int ExamTemplateSessionId { get; set; }
    public ExamTemplateSession TemplateSession { get; set; } = null!;
    public int WorksheetId { get; set; }
    public Worksheet Worksheet { get; set; } = null!;
    public ICollection<MockExamQuestion> Questions { get; set; } = new List<MockExamQuestion>();
}

/// <summary>Maddenin hangi ders satırına sayılacağı.</summary>
public class MockExamQuestion : BaseEntity
{
    public int Id { get; set; }
    public int MockExamSessionId { get; set; }
    public MockExamSession MockExamSession { get; set; } = null!;
    public int ExamTemplateSectionId { get; set; }
    public ExamTemplateSection Section { get; set; } = null!;
    public int WorksheetQuestionId { get; set; }
    public WorksheetQuestion WorksheetQuestion { get; set; } = null!;

    /// <summary>Ders içindeki sıra.</summary>
    public int Order { get; set; }
}

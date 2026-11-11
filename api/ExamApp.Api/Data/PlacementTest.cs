using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1114 (epic #1118): sınıfın ücretsiz seviye tespit testi. Sınıf başına tek; admin soru bankasından üretir (sınıfın sınav
/// derslerine dengeli, yalnız çoktan seçmeli) ve yayımlar. Plan sınırına ve kilidine (#1090) bağlı değildir.
/// </summary>
public class PlacementTest : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int GradeId { get; set; }
    public Grade Grade { get; set; } = null!;

    /// <summary>Boşsa öğrenci görmez.</summary>
    public DateTime? PublishedAtUtc { get; set; }

    public DateTime GeneratedAtUtc { get; set; }

    public List<PlacementTestQuestion> Questions { get; set; } = new();
}

public class PlacementTestQuestion
{
    [Key]
    public int Id { get; set; }

    public int PlacementTestId { get; set; }
    public PlacementTest PlacementTest { get; set; } = null!;

    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;

    public int Order { get; set; }
}

/// <summary>
/// Issue #1114: öğrencinin seviye tespiti — öğrenci ve sınıf başına bir kez. Rapor gönderimde hesaplanıp saklanır (test sonradan yeniden
/// üretilse de rapor değişmez); cevaplar denetim için saklanır.
/// </summary>
public class PlacementAttempt
{
    [Key]
    public int Id { get; set; }

    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public int GradeId { get; set; }

    public int PlacementTestId { get; set; }

    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }

    /// <summary><c>[{ questionId, answerId }]</c>; boş bırakılan answerId null.</summary>
    public string AnswersJson { get; set; } = "[]";

    /// <summary><see cref="ExamApp.Api.Services.Placement.PlacementReportDto"/> JSON'u.</summary>
    public string ReportJson { get; set; } = "{}";

    public DateTime SubmittedAtUtc { get; set; }
}

using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class LearningOutcome : BaseEntity
{
    public int Id { get; set; }


    public string Description { get; set; } // Örn: "Öğrenciler, temel matematik işlemlerini yapabilir."

    public int GradeId { get; set; } // Hangi sınıfa ait olduğu

    public string Code { get; set; } // Örn: "M.3.1.1.1."

    // Issue #692: resmî listedeki kazanım bizim alt konularımızla eşleşmeyebilir; içe aktarımda boş kalır.
    public int? SubTopicId { get; set; } // Hangi alt konuya ait olduğu

    [ForeignKey("SubTopicId")]
    public SubTopic? SubTopic { get; set; } // İlişkili alt konu

    /// <summary>Issue #692: öğretmenin seçim listesi sınıf + derse göre süzülür.</summary>
    public int? SubjectId { get; set; }

    [ForeignKey("SubjectId")]
    public Subject? Subject { get; set; }

    /// <summary>Issue #692: iki müfredat geçiş döneminde birlikte yaşar; aynı kod her müfredatta bir kez bulunur.</summary>
    public CurriculumVersion CurriculumVersion { get; set; } = CurriculumVersion.Previous;

    /// <summary>Issue #692: kazanımın alındığı resmî belge.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(300)]
    public string? SourceReference { get; set; }
    
    public ICollection<LearningOutcomeDetail> Details { get; set; } // İlişkili öğrenme çıktısı detayları

}

/// <summary>Issue #692: kazanımın ait olduğu müfredat.</summary>
public enum CurriculumVersion
{
    Previous = 0,
    Maarif = 1
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1113 (epic #1118): bir sorunun Claude'dan gelen adım adım ipuçları. Soru ve istem sürümü (<see cref="SchemaRevision"/>) başına
/// tek satır, bütün öğrenciler paylaşır: ilk isteyen öğrenci için üretilir, sonrakiler önbellekten okur. Claude yardım edemediyse ya da
/// adımların hepsi cevabı açık ettiği için düştüyse <see cref="Unavailable"/>; yeniden üretilmez.
/// </summary>
public class QuestionHintSet : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;

    public int SchemaRevision { get; set; }

    [MaxLength(100)]
    public string Model { get; set; } = string.Empty;

    public bool Unavailable { get; set; }

    public DateTime GeneratedAtUtc { get; set; }

    public List<QuestionHintStep> Steps { get; set; } = new();
}

public class QuestionHintStep
{
    public const int MaxTextLength = 1000;

    [Key]
    public int Id { get; set; }

    public int HintSetId { get; set; }
    public QuestionHintSet HintSet { get; set; } = null!;

    /// <summary>1'den başlar; öğrenci sırayla açar.</summary>
    public int Step { get; set; }

    [MaxLength(MaxTextLength)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>Issue #1113: öğrencinin açtığı ipucu adımı. Açılan adım yeniden açılınca sayılmaz; aylık hak (<c>step-hints</c>) adım başına düşer.</summary>
public class StudentHintReveal
{
    [Key]
    public int Id { get; set; }

    public int StudentUserId { get; set; }

    public int QuestionId { get; set; }

    public int Step { get; set; }

    public DateTime RevealedAtUtc { get; set; }
}

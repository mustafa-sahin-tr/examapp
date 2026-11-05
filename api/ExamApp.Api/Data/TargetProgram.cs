using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #695: hedef okul (LGS) ya da üniversite programı (YKS) ve o yılın taban puanı. Veriyi admin resmî kaynaktan yükler;
/// aynı program her yıl ayrı satırdır, geçmiş yılın puanı ezilmez.
/// </summary>
public class TargetProgram : BaseEntity
{
    public const int NameMaxLength = 200;
    public const int PlaceMaxLength = 100;
    public const int SourceMaxLength = 300;
    public const int NormalizedKeyMaxLength = 650;

    public int Id { get; set; }

    /// <summary><see cref="TargetScoreTypes"/>; hesaplayıcıdaki (#694) puan türü kimliğiyle aynı.</summary>
    [MaxLength(10)]
    public string ScoreType { get; set; } = string.Empty;

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>YKS'de üniversite; LGS'de boş.</summary>
    [MaxLength(NameMaxLength)]
    public string Institution { get; set; } = string.Empty;

    [MaxLength(PlaceMaxLength)]
    public string City { get; set; } = string.Empty;

    /// <summary>LGS'de aynı adlı okullar çoktur, ilçe ayırır; yoksa boş.</summary>
    [MaxLength(PlaceMaxLength)]
    public string District { get; set; } = string.Empty;

    public int Year { get; set; }

    [Precision(8, 5)]
    public decimal MinimumScore { get; set; }

    [Precision(8, 5)]
    public decimal? Percentile { get; set; }

    [MaxLength(SourceMaxLength)]
    public string SourceReference { get; set; } = string.Empty;

    /// <summary>Türkçe harfler sadeleşmiş ad, kurum, il, ilçe: hem arama hem benzersizlik buradan (DB harmanlaması İ/I'yı eşlemez).</summary>
    [MaxLength(NormalizedKeyMaxLength)]
    public string NormalizedKey { get; set; } = string.Empty;
}

/// <summary>Issue #695: öğrencinin seçtiği tek hedef. Tercih kaydıdır, öğrenci başına en çok bir satır.</summary>
public class StudentTargetGoal
{
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public int TargetProgramId { get; set; }
    public TargetProgram TargetProgram { get; set; } = null!;
    public DateTime SelectedAt { get; set; }
}

public static class TargetScoreTypes
{
    public const string Lgs = "lgs";
    public const string Tyt = "tyt";
    public const string Say = "say";
    public const string Ea = "ea";
    public const string Soz = "soz";

    public const string LgsExam = "lgs";
    public const string YksExam = "yks";

    /// <summary>DİL yok: hesaplayıcı (#694) o puanı üretmez, fark gösterilemez.</summary>
    public static readonly string[] Yks = [Tyt, Say, Ea, Soz];

    public static bool IsExam(string? exam) => exam is LgsExam or YksExam;

    public static string[] OfExam(string exam) => exam == LgsExam ? [Lgs] : Yks;

    public static bool IsValid(string? scoreType) => scoreType == Lgs || Yks.Contains(scoreType);

    /// <summary>Puan türünü hesaplamak için gereken deneme şablonları (<see cref="ExamTemplate.Code"/>): AYT puanı TYT'yi de içerir.</summary>
    public static string[] TemplateCodes(string scoreType) => scoreType switch
    {
        Lgs => ["lgs"],
        Tyt => ["tyt"],
        _ => ["tyt", "ayt"],
    };
}

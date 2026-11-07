using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Models.Dtos;

public class TargetProgramDto
{
    public int Id { get; set; }
    public string ScoreType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Institution { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public int Year { get; set; }
    public decimal MinimumScore { get; set; }
    public decimal? Percentile { get; set; }

    /// <summary>Veli yanıtında boştur: kaynak çoğu zaman bağlantıdır, veli yanıtlarında bağlantı bulunmaz.</summary>
    public string? SourceReference { get; set; }
}

public class AdminTargetProgramDto : TargetProgramDto
{
    /// <summary>En az bir öğrencinin hedefi: silinemez, düzeltilebilir.</summary>
    public bool InUse { get; set; }
}

public class TargetProgramPageDto<T>
{
    public int Total { get; set; }
    public List<T> Items { get; set; } = [];
}

public class SaveTargetProgramDto
{
    [Required, MaxLength(10)]
    public string ScoreType { get; set; } = string.Empty;

    [Required, MaxLength(TargetProgram.NameMaxLength), PlainText]
    public string Name { get; set; } = string.Empty;

    [MaxLength(TargetProgram.NameMaxLength), PlainText]
    public string? Institution { get; set; }

    [Required, MaxLength(TargetProgram.PlaceMaxLength), PlainText]
    public string City { get; set; } = string.Empty;

    [MaxLength(TargetProgram.PlaceMaxLength), PlainText]
    public string? District { get; set; }

    [Range(2000, 2100)]
    public int Year { get; set; }

    [Range(typeof(decimal), "100", "500")]
    public decimal MinimumScore { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? Percentile { get; set; }

    [Required, MaxLength(TargetProgram.SourceMaxLength), PlainText]
    public string SourceReference { get; set; } = string.Empty;
}

public class TargetProgramImportForm
{
    /// <summary><c>lgs</c> ya da <c>yks</c>.</summary>
    [Required, MaxLength(10)]
    public string Exam { get; set; } = string.Empty;

    [Range(2000, 2100)]
    public int Year { get; set; }

    [Required, MaxLength(TargetProgram.SourceMaxLength), PlainText]
    public string Source { get; set; } = string.Empty;

    [Required]
    public IFormFile File { get; set; } = null!;
}

public class TargetProgramImportRowDto
{
    public int Line { get; set; }
    public string ScoreType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Institution { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public decimal MinimumScore { get; set; }
    public decimal? Percentile { get; set; }
}

public class TargetProgramImportChangeDto : TargetProgramImportRowDto
{
    public decimal PreviousMinimumScore { get; set; }
    public decimal? PreviousPercentile { get; set; }
}

/// <summary><see cref="Code"/> ekranda çevrilir: NameRequired, InstitutionRequired, CityRequired, ScoreTypeInvalid, MinimumScoreInvalid, PercentileInvalid, TooLong, InvalidText, Duplicate.</summary>
public class TargetProgramImportErrorDto
{
    public int Line { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
}

/// <summary>Sayılar tam, listeler ilk <c>PreviewLimit</c> satır: resmî dosya on binlerce satır olabilir.</summary>
public class TargetProgramImportPreviewDto
{
    public int NewCount { get; set; }
    public int ChangedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int InvalidCount { get; set; }
    public List<TargetProgramImportRowDto> New { get; set; } = [];
    public List<TargetProgramImportChangeDto> Changed { get; set; } = [];
    public List<TargetProgramImportErrorDto> Invalid { get; set; } = [];
}

public class TargetProgramImportResultDto
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Skipped { get; set; }
}

public class SetTargetGoalDto
{
    [Range(1, int.MaxValue)]
    public int TargetProgramId { get; set; }
}

/// <summary>
/// Hedef ve puan türünün gerektirdiği son tamamlanmış denemeler. Fark istemcide hesaplanır: puan modeli yıllık veridir
/// (#694), formül sunucuya kopyalanmaz. Deneme yoksa <see cref="Results"/> boştur ve fark görünmez.
/// </summary>
public class TargetGoalDto
{
    public TargetProgramDto? Target { get; set; }
    public List<MockExamResultDto> Results { get; set; } = [];

    /// <summary>
    /// Issue #891: öğrencinin sınıf düzeyi (Grade kodundaki sayı, ör. "5-sinif" → 5); okunamazsa null. Arayüz sınav türünü
    /// (5–8 LGS, 9–12 YKS) ve sınava kalan yılı bundan türetir.
    /// </summary>
    public int? GradeLevel { get; set; }
}

public class TargetGoalsStatusDto
{
    public bool Enabled { get; set; }
}

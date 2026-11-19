using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1226: öğretmenin bir soruya kaydettiği çözüm: kalem çizgileri veri olarak (<see cref="StrokesJson"/>, zamanlar sese
/// göre) ve ses dosyası (MinIO'da özel nesne). Video değildir; oynatıcı ses çalarken çizgileri zamanı gelince çizer.
/// Soru başına bir kayıt. Öğrenci yalnız bitmiş testte ya da örnek soruda oynatır (etkileşim çözümüyle aynı kapı, #570).
/// <see cref="BaseEntity"/> değildir: silinen kayıt gerçekten silinir (ses de depodan), yumuşak silme yok.
/// </summary>
public class QuestionSolutionRecording
{
    /// <summary>Çizgi belgesinin şeması (<c>v</c>); biçim değişirse artar, oynatıcı eski kayıtları tanır.</summary>
    public const int CurrentSchemaRevision = 1;

    [Key]
    public int Id { get; set; }

    public int QuestionId { get; set; }
    public Question Question { get; set; } = default!;

    /// <summary>Kaydı yapan öğretmenin (ya da adminin) user id'si.</summary>
    public int RecordedByUserId { get; set; }

    /// <summary>Doğrulanıp yeniden yazılmış çizgi belgesi (bkz. <c>SolutionStrokeDocument</c>).</summary>
    public string StrokesJson { get; set; } = string.Empty;

    /// <summary>MinIO nesnesi (<c>/img/{bucket}/solution-audio/...</c>); tarayıcıya verilmez, ses API ucundan okunur.</summary>
    [MaxLength(500)]
    public string AudioObjectPath { get; set; } = string.Empty;

    [MaxLength(40)]
    public string AudioContentType { get; set; } = string.Empty;

    public long AudioSizeBytes { get; set; }

    public int DurationMs { get; set; }

    public int SchemaRevision { get; set; } = CurrentSchemaRevision;

    public DateTime CreatedAt { get; set; }

    /// <summary>Son kayıt (ilk kayıt ya da yeniden kayıt) zamanı.</summary>
    public DateTime UpdatedAt { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #577: kitaptan bankaya kaydedilen sorunun kaynağı (soru başına en çok bir satır). <see cref="CropSha256"/> MinIO'ya
/// yüklenen soru kırpmasının (question.jpg) baytlarının SHA-256'sıdır; aynı kırpmanın ikinci kaydı kayıttan önce uyarılır.
/// </summary>
public class QuestionSource
{
    public const int MaxLabelLength = 50;

    public int Id { get; set; }

    public int QuestionId { get; set; }

    public Question Question { get; set; } = null!;

    public int BookId { get; set; }

    public Book Book { get; set; } = null!;

    public int FilePage { get; set; }

    /// <summary>Kayıt anındaki ofsetlerden; ofset aralığı yoksa null.</summary>
    public int? PrintedPage { get; set; }

    /// <summary>Etkinlik etiketi, örn. "Etkinlik 9".</summary>
    [MaxLength(MaxLabelLength)]
    public string? ActivityLabel { get; set; }

    /// <summary>Madde etiketi, örn. "3" ya da "b".</summary>
    [MaxLength(MaxLabelLength)]
    public string? ItemLabel { get; set; }

    /// <summary>Küçük harf hex, 64 karakter.</summary>
    [MaxLength(64)]
    public string CropSha256 { get; set; } = string.Empty;
}

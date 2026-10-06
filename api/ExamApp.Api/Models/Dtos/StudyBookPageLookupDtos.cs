using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Services.Storage;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// issue #365 (S3): çalışma etkinliği editörünün JSON kılavuzundaki (<c>[{book, pages}]</c>) kitap sayfalarının
/// <c>study-pages</c> bucket'ında (<c>books/{book}/page_{n}.webp</c>) olup olmadığını sunucu tarafında sorgular.
/// Bucket'lar özel olduğu için tarayıcı nesneyi artık doğrudan yoklayamaz.
/// </summary>
public class StudyBookPageLookupRequestDto
{
    public const int MaxPages = 200;

    [Required]
    [MinLength(1)]
    [MaxLength(MaxPages)]
    public List<StudyBookPageRefDto> Pages { get; set; } = new();
}

public class StudyBookPageRefDto
{
    public const int MaxBookLength = 100;
    public const int MaxPageNumber = 9999;

    /// <summary>Kitap klasörü adı (<c>books/</c> altındaki tek yol parçası). Sunucu sıkı allowlist ile doğrular.</summary>
    [Required]
    [StringLength(MaxBookLength)]
    public string Book { get; set; } = string.Empty;

    [Range(1, MaxPageNumber)]
    public int PageNumber { get; set; }
}

public class StudyBookPageLookupResultDto
{
    public string Book { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public bool Exists { get; set; }

    /// <summary>
    /// Saklanacak biçim (<c>/img/study-pages/books/{book}/page_{n}.webp</c>); kayıtta <c>minioUrl</c> olarak geri gönderilir
    /// (sunucu yine normalize eder). Nesne yoksa null.
    /// </summary>
    [NotStorageUrl("İstemcinin kayıtta geri gönderdiği saklama yolu; erişim vermez, önizleme PreviewUrl'dir (#365)")]
    public string? MinioUrl { get; set; }

    /// <summary>Önizleme için kısa ömürlü imzalı URL (yanıt yazılırken imzalanır). Nesne yoksa null.</summary>
    [StorageUrl(StorageArea.StudyPage)]
    public string? PreviewUrl { get; set; }
}

using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #1122: parçalı PDF yükleme oturumu. Cloudflare ücretsiz plan 100 MB üstü isteği keser (gateway'in Kestrel sınırı 30 MB);
/// tarayıcı dosyayı <see cref="PartSizeBytes"/> parçalara böler, her parça MinIO'da ayrı nesne olur, tamamlanınca sunucuda
/// birleştirilip normal aktarım işi başlar. Geçici kayıttır (soft-delete yok): tamamlanan / iptal edilen oturum silinir, yarım kalanı
/// süpürücü temizler.
/// </summary>
public class BookImportUpload
{
    [Key]
    public Guid Id { get; set; }

    public int OwnerUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    [Required, MaxLength(BookImportJob.MaxBookNameLength)]
    public string BookName { get; set; } = string.Empty;

    public int GradeId { get; set; }

    public int? SubjectId { get; set; }

    [MaxLength(BookImportJob.MaxFileNameLength)]
    public string? FileName { get; set; }

    public long FileSizeBytes { get; set; }

    public long PartSizeBytes { get; set; }

    public int PartCount { get; set; }

    /// <summary>Issue #1144: <see cref="BookImportSource.Images"/> ise parça = sayfa görseli (<see cref="PartCount"/> görsel, boyutu serbest).</summary>
    public BookImportSource Source { get; set; } = BookImportSource.Pdf;
}

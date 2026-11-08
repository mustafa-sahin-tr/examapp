using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>Issue #1018: kitaptan aktarım işinin durumu (takip ekranındaki durum çipi).</summary>
public enum BookImportJobStatus
{
    Queued = 1,
    Running = 2,
    AwaitingConfirmation = 3,
    ReadyForReview = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
}

/// <summary>Issue #1018: aşama sırası takip ekranındaki adımlarla aynıdır (#1017 maketi).</summary>
public enum BookImportStage
{
    Upload = 1,
    Pages = 2,
    Setup = 3,
    SetupConfirmation = 4,
    Questions = 5,
    Classification = 6,
    Review = 7,
    Approval = 8,
}

public enum BookImportTestStatus
{
    Pending = 1,
    Running = 2,
    Done = 3,
    NeedsReview = 4,
    Failed = 5,
}

/// <summary>
/// Issue #1018 (epic #1017): yüklenen kitap PDF'inin asenkron aktarım işi. Hangfire ile çalışır
/// (<see cref="Services.BookImport.BookImportJobRunner"/>; emsal <see cref="QuestionTransferJob"/>). Sahibi
/// <see cref="BaseEntity.CreateUserId"/>; test başına ilerleme <see cref="BookImportJobTest"/> satırlarında (kaldığı testten devam, #1025).
/// </summary>
public class BookImportJob : BaseEntity
{
    public const int MaxBookNameLength = 200;
    public const int MaxFileNameLength = 260;
    public const int MaxMessageLength = 1000;

    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(MaxBookNameLength)]
    public string BookName { get; set; } = string.Empty;

    public int GradeId { get; set; }

    public int? SubjectId { get; set; }

    /// <summary>Kurulum onayından sonra bağlanan kitap kaydı (#577, #1021).</summary>
    public int? BookId { get; set; }

    [ForeignKey(nameof(BookId))]
    public Book? Book { get; set; }

    public BookImportJobStatus Status { get; set; } = BookImportJobStatus.Queued;

    public BookImportStage Stage { get; set; } = BookImportStage.Upload;

    /// <summary>Yüklenen PDF'in MinIO adresi.</summary>
    [Required]
    public string FileUrl { get; set; } = string.Empty;

    [MaxLength(MaxFileNameLength)]
    public string? FileName { get; set; }

    public long FileSizeBytes { get; set; }

    public int? PageCount { get; set; }

    /// <summary>Geçerli aşamanın ilerlemesi (ör. sayfalar 120/412, testler 34/110).</summary>
    public int StageTotal { get; set; }

    public int StageProcessed { get; set; }

    [MaxLength(MaxMessageLength)]
    public string? Message { get; set; }

    [MaxLength(MaxMessageLength)]
    public string? Error { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public ICollection<BookImportJobTest> Tests { get; set; } = new List<BookImportJobTest>();

    public ICollection<BookImportPage> Pages { get; set; } = new List<BookImportPage>();
}

/// <summary>Issue #1018: aktarım işindeki bir kitap testinin durumu (takip ekranındaki test ızgarası).</summary>
public class BookImportJobTest : BaseEntity
{
    public const int MaxTitleLength = 300;

    public int Id { get; set; }

    public Guid JobId { get; set; }

    [ForeignKey(nameof(JobId))]
    public BookImportJob Job { get; set; } = null!;

    /// <summary>Kitaptaki sıra (1'den); ızgara ve devam noktası bu sırayı izler.</summary>
    public int Order { get; set; }

    public int Unit { get; set; }

    public int TestNo { get; set; }

    [MaxLength(MaxTitleLength)]
    public string? Title { get; set; }

    public BookImportTestStatus Status { get; set; } = BookImportTestStatus.Pending;

    public int QuestionsFound { get; set; }

    public int KeyAnswers { get; set; }

    /// <summary>İnceleme işaretleri (#1022), JSON dizi.</summary>
    public string? FlagsJson { get; set; }

    [MaxLength(BookImportJob.MaxMessageLength)]
    public string? Error { get; set; }
}

/// <summary>
/// Issue #1020: PDF'in bir sayfasının görseli (MinIO, özel; <c>book-imports/&lt;jobId&gt;/pages/&lt;filePage&gt;.webp</c>). Sonraki
/// aşamalar (kurulum, test aktarımı) ve kitap oluşunca işaretleme ekranı (<c>bookId</c> + <c>filePage</c>) buradan okur.
/// </summary>
public class BookImportPage
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public BookImportJob Job { get; set; } = null!;

    /// <summary>PDF'teki 1 tabanlı sayfa sırası (kitap kaydındaki "dosya sayfası").</summary>
    public int FilePage { get; set; }

    [MaxLength(1000)]
    public string ImageUrl { get; set; } = string.Empty;

    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }
}

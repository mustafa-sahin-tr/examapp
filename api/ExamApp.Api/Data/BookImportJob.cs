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
    /// <summary>Issue #1024: günlük Claude bütçesi doldu; iş bütçe açılınca kendiliğinden devam eder (<see cref="BookImportJob.ResumeAt"/>).</summary>
    AwaitingBudget = 8,
    /// <summary>Issue #1024: işin maliyet tavanı aşıldı; Admin "Devam et" diyene kadar bekler.</summary>
    AwaitingCostApproval = 9,
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
    /// <summary>Issue #1023: onaylandı; taslak test ve soruları etkin.</summary>
    Approved = 6,
    /// <summary>Issue #1023: incelemede silindi; taslak test ve soruları silindi.</summary>
    Removed = 7,
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

    /// <summary>
    /// Issue #1021: kurulum özeti (JSON, <see cref="Services.BookImport.BookSetupSummary"/>): ofset, cevap anahtarı ve öz
    /// değerlendirme sayfaları, üniteler, eşleşmeyen anahtar satırları. Test satırları <see cref="Tests"/>'te.
    /// </summary>
    public string? SetupJson { get; set; }

    /// <summary>Issue #1021 / #1024: işte harcanan tahmini Claude maliyeti (USD); başarısız çağrılar dahil.</summary>
    [Column(TypeName = "numeric(10,4)")]
    public decimal AiCostUsd { get; set; }

    /// <summary>
    /// Issue #1024: işin maliyet tavanı (USD); null → <c>BookImport:MaxJobCostUsd</c> (0 / boş: tavan yok). Admin tavanı aşan işi
    /// sürdürünce <see cref="AiCostUsd"/> + yapılandırılan tavana yükselir.
    /// </summary>
    [Column(TypeName = "numeric(10,4)")]
    public decimal? CostCeilingUsd { get; set; }

    /// <summary>Issue #1024: bütçe beklenirken işin yeniden deneneceği zaman.</summary>
    public DateTime? ResumeAt { get; set; }

    public ICollection<BookImportJobTest> Tests { get; set; } = new List<BookImportJobTest>();

    public ICollection<BookImportPage> Pages { get; set; } = new List<BookImportPage>();

    public ICollection<BookImportCost> Costs { get; set; } = new List<BookImportCost>();
}

/// <summary>Issue #1018: aktarım işindeki bir kitap testinin durumu (takip ekranındaki test ızgarası).</summary>
public class BookImportJobTest : BaseEntity
{
    public const int MaxTitleLength = 300;
    public const int MaxKeyLength = 100;

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

    /// <summary>Issue #1021: testin dosya sayfaları (kurulum çıkarır, öğretmen onayda düzeltebilir).</summary>
    public int? FileFrom { get; set; }

    public int? FileTo { get; set; }

    public int? Minutes { get; set; }

    /// <summary>Beklenen soru sayısı: anahtar varsa anahtardaki, yoksa sayfada görülen en büyük soru no.</summary>
    public int? ExpectedQuestions { get; set; }

    /// <summary>
    /// Issue #1021: cevap anahtarı, soru sırasıyla bir harf (A–E; okunamayan "?"). Null: anahtar yok (sorular cevapsız aktarılır).
    /// </summary>
    [MaxLength(MaxKeyLength)]
    public string? AnswerKey { get; set; }

    /// <summary>
    /// İki anahtar okumasının farkı, <see cref="AnswerKey"/> ile aynı uzunlukta: "." uyumlu, harf diğer okumanın değeri,
    /// "-" diğer okumada yok. Onayda temizlenir.
    /// </summary>
    [MaxLength(MaxKeyLength)]
    public string? KeyConflicts { get; set; }

    /// <summary>Kurulum onayında oluşturulan kitap testi (#577 kitap kaydı).</summary>
    public int? BookTestId { get; set; }

    [ForeignKey(nameof(BookTestId))]
    public BookTest? BookTest { get; set; }

    /// <summary>
    /// Issue #1023: soru aktarımında oluşturulan taslak test (kaydedilen sorular sırayla bağlı). Onayda etkinleşir; testin
    /// silinmesi yalnız bu testi ve taslak sorularını siler.
    /// </summary>
    public int? WorksheetId { get; set; }

    [ForeignKey(nameof(WorksheetId))]
    public Worksheet? Worksheet { get; set; }

    public BookImportTestStatus Status { get; set; } = BookImportTestStatus.Pending;

    public int QuestionsFound { get; set; }

    public int KeyAnswers { get; set; }

    /// <summary>Issue #1022: taslak olarak kaydedilen soru sayısı.</summary>
    public int QuestionsSaved { get; set; }

    /// <summary>Issue #1022: kırpması bankada zaten olduğu için atlanan soru sayısı (hata değil).</summary>
    public int DuplicatesSkipped { get; set; }

    /// <summary>Kurulum işaretleri (#1021: <c>noKey</c>, <c>keyMismatch</c>, <c>keyConflicts</c>), JSON dizi.</summary>
    public string? FlagsJson { get; set; }

    /// <summary>
    /// Issue #1022: aktarım inceleme işaretleri, sayfa referanslı JSON dizi
    /// (<see cref="Services.BookImport.BookImportReviewFlag"/>).
    /// </summary>
    public string? ReviewFlagsJson { get; set; }

    [MaxLength(BookImportJob.MaxMessageLength)]
    public string? Error { get; set; }
}

/// <summary>
/// Issue #1024: aktarımın bir Claude çağrısının maliyeti (önbellekten dönenler yazılmaz). Takip ekranı Admin'e aşama, test ve
/// model bazında toplar; <see cref="BookImportJob.AiCostUsd"/> bu satırların toplamıdır.
/// </summary>
public class BookImportCost
{
    public const int MaxModelLength = 100;

    public long Id { get; set; }
    public Guid JobId { get; set; }
    public BookImportJob Job { get; set; } = null!;

    public BookImportStage Stage { get; set; }

    /// <summary>Soru aktarımında çağrının testi (<see cref="BookImportJobTest.Id"/>); kurulumda null.</summary>
    public int? TestId { get; set; }

    [Required, MaxLength(MaxModelLength)]
    public string ModelId { get; set; } = string.Empty;

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }

    [Column(TypeName = "numeric(10,6)")]
    public decimal CostUsd { get; set; }

    public DateTime CreatedAt { get; set; }
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

    /// <summary>
    /// Issue #1022: kurulum taramasının okuduğu, sayfada basılı en büyük soru no. Soru aktarımı numaraları sayfa sayfa buna
    /// dayandırır (bir sayfada eksik tespit sonraki sayfaları kaydırmaz) ve eksik numaraları sayfasıyla işaretler.
    /// </summary>
    public int? MaxQuestionNumber { get; set; }
}

using ExamApp.Api.Data;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #1018: kitap PDF'i yükleme formu.</summary>
public class StartBookImportFormDto
{
    public IFormFile? File { get; set; }

    public string? BookName { get; set; }

    public int GradeId { get; set; }

    public int? SubjectId { get; set; }
}

/// <summary>Issue #1122: parçalı yüklemeyi açar; dosya sonra parça parça gelir.</summary>
public class BeginBookImportUploadDto
{
    public string? BookName { get; set; }

    public int GradeId { get; set; }

    public int? SubjectId { get; set; }

    public string? FileName { get; set; }

    public long FileSizeBytes { get; set; }

    /// <summary>Issue #1144: <c>Images</c> ise sayfa başına bir görsel gelir (<see cref="ImageCount"/>; <see cref="FileSizeBytes"/> yok sayılır).</summary>
    public BookImportSource Source { get; set; } = BookImportSource.Pdf;

    public int ImageCount { get; set; }
}

/// <summary>Issue #1052: hızlı ekleme formu; <see cref="Answers"/>[i] i. fotoğrafın isteğe bağlı cevap anahtarı.</summary>
public class StartQuickAddFormDto
{
    public List<IFormFile>? Photos { get; set; }

    public List<string?>? Answers { get; set; }

    public int GradeId { get; set; }

    public int? SubjectId { get; set; }
}

/// <summary>Issue #1018: aktarımlar listesi satırı ve takip ekranının başlığı.</summary>
public class BookImportJobDto
{
    public Guid Id { get; set; }

    /// <summary>Issue #1052: <c>Book</c> ya da <c>QuickAdd</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Issue #1144: <c>Pdf</c> ya da <c>Images</c> (klasörden sayfa görselleri).</summary>
    public string Source { get; set; } = nameof(BookImportSource.Pdf);
    public string BookName { get; set; } = string.Empty;
    public int GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? BookId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    /// <summary>Issue #1032: örnek kitapla demo; AI çağrısı ve hak yok.</summary>
    public bool IsDemo { get; set; }
    /// <summary>Issue #1032: öğretmenlere oynatılan demo kaynağı.</summary>
    public bool IsDemoSource { get; set; }
    public string? FileName { get; set; }
    public long FileSizeBytes { get; set; }
    public int? PageCount { get; set; }
    public int StageTotal { get; set; }
    public int StageProcessed { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>İşin sahibi (Admin listesinde başkalarının işlerini ayırmak için).</summary>
    public int? OwnerId { get; set; }

    /// <summary>Issue #1024: bütçe beklenirken yeniden deneme zamanı.</summary>
    public DateTime? ResumeAt { get; set; }

    /// <summary>
    /// Issue #1025: iş kuyrukta / çalışıyor görünüyor ama <c>BookImport:StallMinutes</c>'tan uzun süredir ilerlemedi
    /// (ör. API yeniden başlatıldı ve Hangfire işi geri almadı); takip ekranı "Yeniden dene" gösterir.
    /// </summary>
    public bool Stalled { get; set; }

    /// <summary>Issue #1024: yalnız Admin'e — işin toplam Claude maliyeti ve geçerli tavanı (null: tavan yok).</summary>
    public decimal? AiCostUsd { get; set; }
    public decimal? CostCeilingUsd { get; set; }
}

/// <summary>Issue #1018: takip ekranı — iş + test satırları.</summary>
public class BookImportJobDetailDto : BookImportJobDto
{
    public List<BookImportJobTestDto> Tests { get; set; } = new();

    /// <summary>Issue #1024: yalnız Admin'e — aşama ve model bazında maliyet (Admin değilse boş).</summary>
    public List<BookImportCostDto> Costs { get; set; } = new();
}

/// <summary>Issue #1024: bir aşamanın bir modelle yaptığı Claude çağrılarının toplamı.</summary>
public class BookImportCostDto
{
    public string Stage { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int Calls { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
}

public class BookImportJobTestDto
{
    /// <summary>Issue #1023: inceleme işlemleri (onay, silme, cevap) bu id ile.</summary>
    public int Id { get; set; }
    public int Order { get; set; }
    public int Unit { get; set; }
    public int TestNo { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public int QuestionsFound { get; set; }
    public int KeyAnswers { get; set; }
    public List<string> Flags { get; set; } = new();

    /// <summary>Issue #1022: taslak kaydedilen ve kırpma tekrarı olduğu için atlanan soru sayısı.</summary>
    public int QuestionsSaved { get; set; }
    public int DuplicatesSkipped { get; set; }

    /// <summary>Issue #1022: aktarım inceleme işaretleri (kod, dosya sayfaları, soru numaraları).</summary>
    public IReadOnlyList<Services.BookImport.BookImportReviewFlag> ReviewFlags { get; set; } = [];

    /// <summary>Issue #1023: taslak test (önizleme / işaretleme bağlantısı), dosya sayfaları ve cevapsız soru sayısı (onayı engeller).</summary>
    public int? WorksheetId { get; set; }
    public int? FileFrom { get; set; }
    public int? FileTo { get; set; }
    public int Answerless { get; set; }

    /// <summary>Issue #1024: yalnız Admin'e — testin soru aktarımı maliyeti.</summary>
    public decimal? CostUsd { get; set; }

    public string? Error { get; set; }
}

/// <summary>Issue #1023: incelemedeki testin sorusu (cevapsızlar için satır içi A–D seçici).</summary>
public class BookImportReviewQuestionDto
{
    public int Id { get; set; }
    public string? Number { get; set; }
    public int FilePage { get; set; }
    public int OptionCount { get; set; }
    /// <summary>Önizleme: kırpılmış soru görseli.</summary>
    [Services.Storage.StorageUrl(Services.Storage.StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }
    /// <summary>Issue #1062: sorunun pasaj görseli (kitaptaki uyaran kutusu); yoksa null.</summary>
    [Services.Storage.StorageUrl(Services.Storage.StorageArea.QuestionImage)]
    public string? PassageImageUrl { get; set; }
    /// <summary>Doğru şıkkın sırası (A = 0); cevapsızsa null.</summary>
    public int? AnswerIndex { get; set; }
}

/// <summary>Issue #1032: öğretmenin demo durumu; <c>Available</c> kaynak seçili ve hak kaldıysa ya da süren demo varsa true.</summary>
public class BookImportDemoDto
{
    public bool Available { get; set; }
    public string? BookName { get; set; }
    public int Used { get; set; }
    public int Limit { get; set; }
    /// <summary>Bitmemiş demo: yeni başlatmak yerine buna dönülür.</summary>
    public Guid? ActiveJobId { get; set; }
}

public class SetBookImportDemoSourceDto
{
    public bool IsDemoSource { get; set; }
}

public class SetBookImportAnswerDto
{
    public int OptionIndex { get; set; }
}

/// <summary>Issue #1023: "Sorunsuz testleri onayla" sonucu; atlananlar ve nedeni (answerless / failed / notDraft).</summary>
public class ApproveAllBookImportResultDto
{
    public List<int> Approved { get; set; } = new();
    public List<BookImportSkippedTestDto> Skipped { get; set; } = new();
}

public record BookImportSkippedTestDto(int Id, string Reason);

/// <summary>Issue #1018 / #1019: başlangıç ekranındaki "Bu ay N/M kitap" kartı (Limit null = sınırsız).</summary>
public class BookImportQuotaDto
{
    public int? Limit { get; set; }
    public int Used { get; set; }
    public DateTime? ResetsAtUtc { get; set; }
    public bool Allowed { get; set; }

    /// <summary>Kullanıcının planı (#1019; ör. <c>free</c>). Sınır 0 ise arayüz demoya (#1032) yönlendirir.</summary>
    public string? PlanKey { get; set; }
}

/// <summary>Issue #1021: kurulum onay ekranı — kurulum özeti, üniteler ve düzenlenebilir test tablosu.</summary>
public class BookImportSetupDto
{
    /// <summary>Issue #1032: düzenlemeler kaydedilmez, kitap kaydı yazılmaz.</summary>
    public bool IsDemo { get; set; }
    public Guid JobId { get; set; }
    public string BookName { get; set; } = string.Empty;
    public int? PageCount { get; set; }

    /// <summary>Basılı = dosya + ofset; null: basılı sayfa no okunamadı.</summary>
    public int? Offset { get; set; }

    public List<BookImportPageRangeDto> AnswerKeyRanges { get; set; } = new();
    public List<int> SelfAssessmentPages { get; set; } = new();
    public List<BookImportSetupUnitDto> Units { get; set; } = new();
    public List<string> OrphanKeys { get; set; } = new();

    /// <summary>Kitapta cevap anahtarı bulunamadı: sorular cevapsız aktarılır (devam / iptal).</summary>
    public bool NoAnswerKey { get; set; }

    public decimal AiCostUsd { get; set; }
    public int SchemaRevision { get; set; }

    /// <summary>Aynı adlı mevcut kitap (onayda "mevcut kitabı kullan" ya da yeni ad seçilir).</summary>
    public BookImportBookRefDto? ExistingBook { get; set; }

    public List<BookImportSetupTestDto> Tests { get; set; } = new();
}

public record BookImportPageRangeDto(int FileFrom, int FileTo);

public record BookImportBookRefDto(int Id, string Name);

public class BookImportSetupUnitDto
{
    public int Unit { get; set; }
    public string? Title { get; set; }
    public int FileFrom { get; set; }
    public int FileTo { get; set; }
    public int? TopicId { get; set; }
    public string? TopicName { get; set; }
}

public class BookImportSetupTestDto
{
    public int Order { get; set; }
    public int Unit { get; set; }
    public int TestNo { get; set; }
    public string? Title { get; set; }
    public int? Minutes { get; set; }
    public int? FileFrom { get; set; }
    public int? FileTo { get; set; }
    public int? ExpectedQuestions { get; set; }

    /// <summary>Soru sırasıyla harf (A–E, okunamayan "?"); null anahtar yok.</summary>
    public string? AnswerKey { get; set; }

    /// <summary>"." uyumlu; harf ikinci okumanın değeri; "-" ikinci okumada yok.</summary>
    public string? KeyConflicts { get; set; }

    public List<string> Flags { get; set; } = new();
}

/// <summary>Issue #1021: kurulum onayı. Listede olmayan test aktarılmaz; anahtar boş/null ise test cevapsız aktarılır.</summary>
public class ConfirmBookImportSetupDto
{
    public string? BookName { get; set; }

    /// <summary>Aynı adlı mevcut kitabı kullan (kayıt kümeleri boşsa kurulumla doldurulur).</summary>
    public int? ExistingBookId { get; set; }

    public List<ConfirmBookImportTestDto> Tests { get; set; } = new();
}

public class ConfirmBookImportTestDto
{
    public int Order { get; set; }
    public string? Title { get; set; }
    public int? Minutes { get; set; }
    public int FileFrom { get; set; }
    public int FileTo { get; set; }
    public string? AnswerKey { get; set; }
}

/// <summary>Issue #1023 "İşaretlemede düzelt": sayfanın işaretleme ekranında düzeltilen soruları (boş = sayfanın taslaklarını sil).</summary>
public class ReplaceBookImportPageDto
{
    [System.ComponentModel.DataAnnotations.MaxLength(Services.Labeling.LabelingBankService.MaxQuestions)]
    public List<Labeling.LabelingBankQuestionDto> Questions { get; set; } = new();
}

/// <summary>Issue #1023: sayfa düzeltmesinin sonucu — kaydedilen, kırpması bankada olduğu için atlanan, silinen eski taslak ve cevapsız soru sayısı.</summary>
public class BookImportPageFixDto
{
    public int Saved { get; set; }
    public int Duplicates { get; set; }
    public int Removed { get; set; }
    public int Answerless { get; set; }
}

/// <summary>Issue #1056: kitap aktarımı ve hızlı ekleme maliyet raporu (yalnız Admin).</summary>
public class BookImportCostReportDto
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public BookImportCostTotalsDto Totals { get; set; } = new();
    public List<BookImportCostUserDto> Users { get; set; } = [];
    public int JobCount { get; set; }
    public List<BookImportCostJobDto> Jobs { get; set; } = [];
    public List<BookImportCostDto> Stages { get; set; } = [];
}

public class BookImportCostTotalsDto
{
    public int Jobs { get; set; }
    public int Pages { get; set; }
    public int QuestionsSaved { get; set; }
    public decimal CostUsd { get; set; }

    /// <summary>Kaydedilen soru yoksa null.</summary>
    public decimal? CostPerQuestionUsd { get; set; }
}

public class BookImportCostUserDto
{
    public int UserId { get; set; }

    /// <summary>auth-api çözemezse null; ekran kullanıcı id'sini gösterir.</summary>
    public string? FullName { get; set; }
    public string? Role { get; set; }
    public string? PlanKey { get; set; }
    public string? PlanName { get; set; }
    public int Jobs { get; set; }
    public decimal CostUsd { get; set; }
}

public class BookImportCostJobDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string BookName { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Pages { get; set; }
    public int QuestionsSaved { get; set; }

    /// <summary>İşin yalnız seçilen aralıktaki çağrılarının maliyeti.</summary>
    public decimal CostUsd { get; set; }
}

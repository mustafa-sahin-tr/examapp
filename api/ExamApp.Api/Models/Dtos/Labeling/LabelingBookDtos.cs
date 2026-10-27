using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos.Labeling;

// Issue #577: kitap kaydı (işaretleme ekranı, Admin + QuestionTrainer). Tüm sayfa numaraları DOSYA sayfasıdır (1'den
// başlar); bastırılmış sayfa = dosya sayfası + offset.

/// <summary>Ofset aralığı: dosya sayfası [FileFrom, FileTo] → bastırılmış = dosya + Offset (negatif olabilir).</summary>
public class LabelingBookOffsetDto
{
    public int FileFrom { get; set; }

    public int FileTo { get; set; }

    public int Offset { get; set; }
}

/// <summary>Dosya sayfa aralığı (uçlar dahil) — cevap anahtarı sayfaları.</summary>
public class LabelingBookPageRangeDto
{
    public int FileFrom { get; set; }

    public int FileTo { get; set; }
}

/// <summary>İçindekiler satırı (yazma). Konu dersin ve sınıfın, alt konu konunun olmalı; alt konu konu ister.</summary>
public class LabelingBookTocInputDto
{
    public int FileFrom { get; set; }

    public int FileTo { get; set; }

    public int GradeId { get; set; }

    public int SubjectId { get; set; }

    public int? TopicId { get; set; }

    public int? SubTopicId { get; set; }
}

/// <summary>İçindekiler satırı (okuma) — gösterim için adlar ve aralığın bastırılmış karşılığı (ofset yoksa null).</summary>
public record LabelingBookTocDto(
    int FileFrom,
    int FileTo,
    int? PrintedFrom,
    int? PrintedTo,
    int GradeId,
    string? GradeName,
    int SubjectId,
    string? SubjectName,
    int? TopicId,
    string? TopicName,
    int? SubTopicId,
    string? SubTopicName);

/// <summary><c>GET api/labeling/books</c> / <c>{id}</c> — kitap ve tüm kümeleri (FileFrom sırasıyla).</summary>
public record LabelingBookDto(
    int Id,
    string Name,
    int? CanonicalBookId,
    string? CanonicalBookName,
    string? Notes,
    IReadOnlyList<LabelingBookOffsetDto> Offsets,
    IReadOnlyList<LabelingBookPageRangeDto> AnswerKeyRanges,
    IReadOnlyList<LabelingBookTocDto> Toc);

/// <summary>
/// <c>POST api/labeling/books</c> / <c>PUT api/labeling/books/{id}</c>. PUT kümeleri bütün olarak değiştirir (verilmeyen
/// küme boşaltılır). Aralıklar küme içinde çakışamaz; FileFrom ≤ FileTo; ilk bastırılmış sayfa ≥ 1.
/// </summary>
public class LabelingBookSaveRequestDto
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Aynı içeriğin asıl kitabı (tek seviye: asıl kaydın kendisi başka kitaba bağlı olamaz; kendisi olamaz).</summary>
    public int? CanonicalBookId { get; set; }

    [MaxLength(Data.Book.MaxNotesLength)]
    public string? Notes { get; set; }

    [MaxLength(200)]
    public List<LabelingBookOffsetDto> Offsets { get; set; } = new();

    [MaxLength(100)]
    public List<LabelingBookPageRangeDto> AnswerKeyRanges { get; set; } = new();

    [MaxLength(1000)]
    public List<LabelingBookTocInputDto> Toc { get; set; } = new();
}

/// <summary>
/// <c>GET api/labeling/books/{id}/pages/{filePage}</c> — dosya sayfasının bilgisi. <c>TocStale</c>: sayfanın içindekiler
/// satırı silinmiş bir sınıf/ders/konu/alt konuya işaret ediyor (<c>Toc</c> null; kitap kaydı düzeltilmeli).
/// </summary>
public record LabelingBookPageDto(int FilePage, int? PrintedPage, bool IsAnswerKey, LabelingBookTocDto? Toc, bool TocStale = false);

/// <summary>
/// <c>GET api/labeling/books/{id}/printed/{printedPage}?near=</c> — bastırılmış sayfaya karşılık gelen dosya sayfaları.
/// Fasiküllü kitapta birden çok olabilir; <c>near</c> verilirse ona en yakın önce, yoksa artan sırada.
/// </summary>
public record LabelingBookPrintedPageDto(int PrintedPage, IReadOnlyList<int> FilePages);

/// <summary>
/// 409 <c>LabelingCropAlreadyInBank</c> içindeki mevcut kayıt. Çağıranın göremediği soru (sahibi başkası, çağıran Admin
/// değil) yanıtta yalnız <c>{questionIndex, otherOwner: true}</c> olarak döner (<see cref="ToResponse"/>).
/// </summary>
public record LabelingCropDuplicateDto(
    int QuestionIndex,
    int QuestionId,
    string BookName,
    int FilePage,
    int? PrintedPage,
    string? ActivityLabel,
    string? ItemLabel,
    bool OtherOwner = false)
{
    /// <summary>Yanıt gövdesi: görünürse tüm alanlar (<c>otherOwner: false</c>), değilse yalnız sıra + <c>otherOwner: true</c>.</summary>
    public object ToResponse() => OtherOwner
        ? new LabelingCropDuplicateHiddenDto(QuestionIndex)
        : this;
}

/// <summary>Başka kullanıcının sorusuyla çakışan kırpma — kimlik/kitap/sayfa bilgisi sızdırılmaz.</summary>
public record LabelingCropDuplicateHiddenDto(int QuestionIndex)
{
    public bool OtherOwner => true;
}

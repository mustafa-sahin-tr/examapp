using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #933: çalışma sayfasının sıralı içerik bloğu. Sayfanın ana içeriği (<see cref="StudyItem.ContentType"/>:
/// görsel seti / bağlantı / kitap aralığı) program, takvim ve kitap kaydı tarafından okunduğu için yerinde kalır;
/// bloklar sayfa gövdesidir ve ana içerikten sonra sırayla gösterilir.
/// </summary>
public class StudyItemBlock : BaseEntity
{
    public const int MaxTextLength = 4000;
    public const int MaxBlocksPerItem = 50;

    [Key]
    public int Id { get; set; }

    public int StudyItemId { get; set; }

    [ForeignKey("StudyItemId")]
    public StudyItem StudyItem { get; set; } = default!;

    public int SortOrder { get; set; }

    public StudyItemBlockType Type { get; set; }

    /// <summary>Text: gövde; Image: alt yazı; Link: başlık; Flashcard: ön yüz.</summary>
    [MaxLength(MaxTextLength)]
    public string? Text { get; set; }

    /// <summary>Yalnız Flashcard: arka yüz.</summary>
    [MaxLength(MaxTextLength)]
    public string? BackText { get; set; }

    /// <summary>Yalnız Image: <c>/img/study-pages/pages/...</c>, yanıtta imzalanır.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Yalnız Link: http/https adresi.</summary>
    public string? Url { get; set; }

    public StudyItemLinkPlatform? Platform { get; set; }

    /// <summary>Yalnız Question: soru bankasındaki soru; soru silinirse blok kalır, öğrenciye gösterilmez.</summary>
    public int? QuestionId { get; set; }

    [ForeignKey("QuestionId")]
    public Question? Question { get; set; }
}

/// <summary>DB'de int saklanır; değerleri değiştirme.</summary>
public enum StudyItemBlockType
{
    Text = 0,
    Image = 1,
    Link = 2,
    Question = 3,
    Flashcard = 4
}

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Çalışma etkinliği. Eski adı StudyPage; issue #139 ile çok tipe (resim / link / kitap sayfa aralığı) genişletildi.
/// </summary>
public class StudyItem : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int? GradeId { get; set; }

    public int? SubjectId { get; set; }

    [ForeignKey("SubjectId")]
    public Subject? Subject { get; set; }

    public int? TopicId { get; set; }

    [ForeignKey("TopicId")]
    public Topic? Topic { get; set; }

    public int? SubTopicId { get; set; }

    [ForeignKey("SubTopicId")]
    public SubTopic? SubTopic { get; set; }

    public bool IsPublished { get; set; } = true;

    public int CreatedByUserId { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public string CreatedByRole { get; set; } = string.Empty;

    // --- İçerik tipi (issue #139) ---

    public StudyItemContentType ContentType { get; set; } = StudyItemContentType.Image;

    // Link tipi alanları — sadece ContentType == Link iken dolu
    public string? Url { get; set; }

    public StudyItemLinkPlatform? Platform { get; set; }

    // BookPageRange tipi alanları — sadece ContentType == BookPageRange iken dolu
    public int? BookId { get; set; }

    [ForeignKey("BookId")]
    public Book? Book { get; set; }

    public int? BookTestId { get; set; }

    [ForeignKey("BookTestId")]
    public BookTest? BookTest { get; set; }

    public int? StartPage { get; set; }

    public int? EndPage { get; set; }

    public ICollection<StudyItemImage> Images { get; set; } = new List<StudyItemImage>();
}

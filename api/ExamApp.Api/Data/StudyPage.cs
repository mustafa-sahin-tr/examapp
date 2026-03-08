using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class StudyPage : BaseEntity
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

    public ICollection<StudyPageImage> Images { get; set; } = new List<StudyPageImage>();
}

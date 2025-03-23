using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;

public class SubTopic : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } // Örn: "Görsel Yorumlama 1"

    [Required]
    public int TopicId { get; set; }

    [ForeignKey("TopicId")]
    public Topic Topic { get; set; }

    public ICollection<QuestionSubTopic> QuestionSubTopics { get; set; } = new List<QuestionSubTopic>(); // New collection for many-to-many relationship
}

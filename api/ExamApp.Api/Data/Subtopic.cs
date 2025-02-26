using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class SubTopic
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } // Örn: "Görsel Yorumlama 1"

    [Required]
    public int TopicId { get; set; }

    [ForeignKey("TopicId")]
    public Topic Topic { get; set; }
}

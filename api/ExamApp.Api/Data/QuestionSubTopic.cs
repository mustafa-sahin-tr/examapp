using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class QuestionSubTopic : BaseEntity
{
    [Key]
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question Question { get; set; }

    public int SubTopicId { get; set; }
    public SubTopic SubTopic { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class TestPrototypeDetail
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TestPrototypeId { get; set; }

    [ForeignKey("TestPrototypeId")]
    public TestPrototype TestPrototype { get; set; }

    [Required]
    public int SubjectId { get; set; }

    [ForeignKey("SubjectId")]
    public Subject Subject { get; set; }

    [Required]
    public int QuestionCount { get; set; } // Bu dersten kaç soru olacağı
}

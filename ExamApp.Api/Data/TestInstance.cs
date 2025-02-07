using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;

public class TestInstance
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int StudentId { get; set; }

    [ForeignKey("StudentId")]
    public Student Student { get; set; }

    [Required]
    public int TestId { get; set; }

    [ForeignKey("TestId")]
    public Test Test { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    public ICollection<TestInstanceQuestion> TestInstanceQuestions { get; set; }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;

public class TestInstanceQuestion
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TestInstanceId { get; set; }

    [ForeignKey("TestInstanceId")]
    public TestInstance TestInstance { get; set; }

    [Required]
    public int TestQuestionId { get; set; }

    [ForeignKey("TestQuestionId")]
    public TestQuestion TestQuestion { get; set; }

    public int? SelectedAnswerId { get; set; }

    [ForeignKey("SelectedAnswerId")]
    public Answer SelectedAnswer { get; set; }

    public bool IsCorrect { get; set; }
    public int TimeTaken { get; set; } // Kaç saniyede çözüldü
    public bool ShowCorrectAnswer { get; set; } // Kullanıcı "Sonucu Gör" yaptı mı?

}

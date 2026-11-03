namespace ExamApp.Api.Data;

/// <summary>Issue #532: öğrencinin sonra çözmek için kaydettiği soru. Öğrenci + soru başına tek satır.</summary>
public class SavedQuestion : BaseEntity
{
    public int Id { get; set; }

    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public int QuestionId { get; set; }
    public Question Question { get; set; } = default!;
}

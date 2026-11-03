namespace ExamApp.Api.Data;

/// <summary>Issue #698: öğrencinin yanlış nedeni etiketi. İsteğe bağlıdır; satır yoksa etiket yok demektir.</summary>
public enum MistakeReason
{
    DidNotKnow = 0,
    Calculation = 1,
    Attention = 2,
    Time = 3
}

public class StudentMistakeReason : BaseEntity
{
    public int Id { get; set; }

    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;

    public int QuestionId { get; set; }
    public Question Question { get; set; } = default!;

    public MistakeReason Reason { get; set; }
}

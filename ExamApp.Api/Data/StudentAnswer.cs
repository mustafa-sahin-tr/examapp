using System;

namespace ExamApp.Api.Data;

public class StudentAnswer
{
    public int Id { get; set; }
    
    public int StudentId { get; set; }  // Hangi öğrenci çözdü
    public Student Student { get; set; }

    public int QuestionId { get; set; }  // Hangi soruya cevap verdi
    public Question Question { get; set; }

    public int SelectedAnswerId { get; set; }  // Öğrencinin seçtiği şık
    public Answer SelectedAnswer { get; set; }

    public bool IsCorrect { get; set; }  // Doğru mu yanlış mı

    public int TimeTaken { get; set; }  // Soruyu çözme süresi (saniye cinsinden)
    
    public DateTime AnsweredAt { get; set; }  // Ne zaman çözüldü
}

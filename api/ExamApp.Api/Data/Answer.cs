using System;

namespace ExamApp.Api.Data;

public class Answer
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;  // Şık metni
    public string? ImageUrl { get; set; }  // Şık resimli ise
    
    public int QuestionId { get; set; }
    public Question Question { get; set; }

}


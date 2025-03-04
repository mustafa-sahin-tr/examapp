using System;

namespace ExamApp.Api.Data;

public class Answer
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;  // Şık metni
    public string? ImageUrl { get; set; }  // Şık resimli ise
    
    public int QuestionId { get; set; }
    public Question Question { get; set; }

    public double? X { get; set; }

    public double? Y { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public bool IsCanvasQuestion { get; set; } = false;


}


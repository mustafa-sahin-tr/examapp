using System;

namespace ExamApp.Api.Data;

public class Answer : BaseEntity
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

    // Sıralama ve etiket
    public string? Tag { get; set; } // A, B, C, D, E
    public int? Order { get; set; } // 0, 1, 2, ...


}


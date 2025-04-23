public class QuestionDto
{
    public int Id { get; set; }
    public string Text { get; set; }
    public string? SubText { get; set; }
    public string? ImageUrl { get; set; }
    public string CategoryName { get; set; }

    public int? SubjectId { get; set; }

    public int? TopicId { get; set; } // Konu ID'si
    public int Point { get; set; }
    public bool IsExample { get; set; }
    public string? PracticeCorrectAnswer { get; set; }
    public int AnswerColCount { get; set; }

    public bool IsCanvasQuestion { get; set; } // Canvas sorusu mu? (Evet/Hayır)
    public PassageDto? Passage { get; set; }
    public List<AnswerDto> Answers { get; set; } = new();

    public int? CorrectAnswerId { get; set; } // Doğru cevap ID'si
    public string? CorrectAnswer { get; set; } // Doğru cevap ID'si

    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}

public class PassageDto
{
    public int? Id { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? ImageUrl { get; set; }    
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}
public class AnswerDto
{
    public int? Id { get; set; }
    public string? Text { get; set; }
    public string? Image { get; set; } // Base64 formatında geliyor
    public string? ImageUrl { get; set; }    

    public bool IsCorrect { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
}

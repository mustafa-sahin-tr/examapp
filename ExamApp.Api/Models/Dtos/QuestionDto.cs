public class QuestionDto
{
    public string? Text { get; set; }
    public string? SubText { get; set; }
    public string? Image { get; set; } // Base64 formatında geliyor
    public int SubjectId { get; set; }
    public int TopicId { get; set; }
    public int SubTopicId { get; set; }

    public int Point { get; set; }
    public int CorrectAnswer { get; set; }
    public List<AnswerDto> Answers { get; set; }
}

public class AnswerDto
{
    public string? Text { get; set; }
    public string? Image { get; set; } // Base64 formatında geliyor
}

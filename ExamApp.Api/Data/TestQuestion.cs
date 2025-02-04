using System;

namespace ExamApp.Api.Data;

public class TestQuestion
{
    public int TestId { get; set; }
    public ExamTest Test { get; set; }

    public int QuestionId { get; set; }
    public Question Question { get; set; }
}


using System;

namespace ExamApp.Contracts;

public class AnswerSubmittedEvent
{
    public int UserId { get; set; }
    public int QuestionId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public int TimeTakenInSeconds { get; set; }
}
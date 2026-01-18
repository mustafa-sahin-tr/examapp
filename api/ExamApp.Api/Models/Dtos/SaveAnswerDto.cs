using System;

namespace ExamApp.Api.Models.Dtos;

public class SaveAnswerDto
{
    public int TestInstanceId { get; set; }
    public int TestQuestionId { get; set; }
    public int SelectedAnswerId { get; set; }
    public string? AnswerPayload { get; set; } // Added for drag-drop answers
    public int TimeTaken { get; set; } // in seconds
}

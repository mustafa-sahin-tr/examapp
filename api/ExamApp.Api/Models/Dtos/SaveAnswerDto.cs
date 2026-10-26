using System;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Models.Dtos;

public class SaveAnswerDto
{
    public int TestInstanceId { get; set; }
    public int TestQuestionId { get; set; }
    public int SelectedAnswerId { get; set; }
    // Added for drag-drop answers. review #571: en çok 16 KB (servis de denetler → 400 InvalidAnswerPayload).
    [MaxLength(InteractionPayload.MaxLength)]
    public string? AnswerPayload { get; set; }
    public int TimeTaken { get; set; } // in seconds
}

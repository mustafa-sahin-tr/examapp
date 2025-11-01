using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

public class WorksheetAssignmentRequestDto
{
    [Required]
    public int WorksheetId { get; set; }

    public int? StudentId { get; set; }

    public int? GradeId { get; set; }

    [Required]
    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }
}

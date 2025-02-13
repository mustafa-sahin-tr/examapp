using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

public class ExamDto
{
    
    public int? Id { get; set; }
    public required string Name { get; set; }

    public string? Description { get; set; }

    public required int GradeId { get; set; }

    public required int MaxDurationSeconds { get; set; }

    public bool IsPracticeTest { get; set; }
}

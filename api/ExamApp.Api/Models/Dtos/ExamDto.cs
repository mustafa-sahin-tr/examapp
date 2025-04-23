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

    public string? Subtitle { get; set; }

    public string? ImageUrl { get; set; }

    public string? BadgeText { get; set; }


    public int? BookTestId { get; set; }
    public int? BookId { get; set; }

    public string? NewBookName { get; set; }
    public string? NewBookTestName { get; set; }

}

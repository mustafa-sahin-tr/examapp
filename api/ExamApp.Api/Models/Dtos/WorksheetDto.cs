using System;

namespace ExamApp.Api.Models.Dtos;

public class WorksheetDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int MaxDurationSeconds { get; set; }
    public bool IsPracticeTest { get; set; }
    public string? Subtitle { get; set; }
    public string? ImageUrl { get; set; }
    public string? BadgeText { get; set; }
    public int? BookTestId { get; set; }
    public int QuestionCount { get; set; } // ✅ Eklenen alan
}
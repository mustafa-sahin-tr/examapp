using System;

namespace ExamApp.Foundation.Contracts;

public class StudyPageImageUploadedEvent
{
    public string ImageUrl { get; set; } = string.Empty;
    public string? ClassificationSource { get; set; }
    public DateTime CreatedAt { get; set; }
}

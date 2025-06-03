using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Dtos;

public class BulkExamCreateDto
{
    public List<BulkExamItemDto> Exams { get; set; } = new List<BulkExamItemDto>();
}

public class BulkExamItemDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public int GradeId { get; set; }

    [Required]
    public int MaxDurationSeconds { get; set; }

    public bool IsPracticeTest { get; set; } = false;

    public string? Subtitle { get; set; }

    public string? BadgeText { get; set; }

    public int? BookTestId { get; set; }

    public int? BookId { get; set; }

    public string? NewBookName { get; set; }

    public string? NewBookTestName { get; set; }

    public int? SubjectId { get; set; }


    public int? TopicId { get; set; }


    public int? SubTopicId { get; set; }



}

public class BulkExamResultDto : ResponseBaseDto
{
    public List<ExamSavedDto> SuccessfulExams { get; set; } = new List<ExamSavedDto>();
    public List<BulkExamErrorDto> FailedExams { get; set; } = new List<BulkExamErrorDto>();
    public int TotalProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
}

public class BulkExamErrorDto
{
    public string ExamName { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public int RowNumber { get; set; }
}

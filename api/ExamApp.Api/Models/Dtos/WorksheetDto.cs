using System;

namespace ExamApp.Api.Models.Dtos;

public class WorksheetDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int GradeId { get; set; }
    public int? SubjectId { get; set; }

    public int? TopicId { get; set; } // Konu ID'si (isteğe bağlı)
    public int? SubTopicId { get; set; } // Alt konu ID'si (isteğe bağlı)
    public int MaxDurationSeconds { get; set; }
    public bool IsPracticeTest { get; set; }
    public string? Subtitle { get; set; }
    public string? ImageUrl { get; set; }
    public string? BadgeText { get; set; }
    public int? BookTestId { get; set; }
    public int? BookId { get; set; }
    public int QuestionCount { get; set; } // ✅ Eklenen alan

    public InstanceSummaryDto? Instance { get; set; }

    public int InstanceCount { get; set; } = 0;// ✅ Eklenen alan = 0

}

public class WorksheetWithInstanceDto
{
    public WorksheetDto Worksheet { get; set; } = default!;
    public WorksheetInstance? Instance { get; set; }
}

public class WorksheetInstanceDto
{
    public int Id { get; set; }
    public string TestName { get; set; } = default!;
    public WorksheetInstanceStatus Status { get; set; }
    public int MaxDurationSeconds { get; set; }
    public bool IsPracticeTest { get; set; }

    public List<WorksheetInstanceQuestionDto> TestInstanceQuestions { get; set; } = new();
}

public class WorksheetInstanceQuestionDto
{
    public int Id { get; set; }
    public int Order { get; set; }
    public QuestionDto Question { get; set; } = default!;
    public int? SelectedAnswerId { get; set; }
    public string? AnswerPayload { get; set; }
    public int? TimeTaken { get; set; }
}

public class WorksheetInstanceResultDto
{
    public int Id { get; set; }
    public string TestName { get; set; } = default!;
    public WorksheetInstanceStatus Status { get; set; }
    public int MaxDurationSeconds { get; set; }
    public bool IsPracticeTest { get; set; }
    public List<WorksheetInstanceQuestionDto> TestInstanceQuestions { get; set; } = new();
}

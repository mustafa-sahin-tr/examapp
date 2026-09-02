using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

public class WorksheetDetailDto
{
    public WorksheetDto Worksheet { get; set; } = default!;
    public WorksheetStatsDto Stats { get; set; } = new();
    public List<WorksheetTopicBreakdownDto> TopicBreakdown { get; set; } = new();
    public List<string> Outcomes { get; set; } = new();
    public string? RewardBadgeText { get; set; }
    public WorksheetSampleQuestionDto? SampleQuestion { get; set; }
    public List<WorksheetAttemptDto> Attempts { get; set; } = new();
    public int? ImprovementPoints { get; set; }
    public List<SimilarWorksheetDto> SimilarWorksheets { get; set; } = new();
    public WorksheetTeacherInsightsDto? TeacherInsights { get; set; }
    public WorksheetCompletedResultDto? CompletedResult { get; set; }

    /// <summary>Öğrencinin bu worksheet için planladığı hatırlatma (varsa). Öğretmen için null.</summary>
    public WorksheetReminderDto? PlannedReminder { get; set; }
}

public class WorksheetStatsDto
{
    public int SolverCount { get; set; }
    public int? AverageScorePercent { get; set; }
}

public class WorksheetTopicBreakdownDto
{
    public int? TopicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public int WeightPercent { get; set; }
}

public class WorksheetSampleQuestionDto
{
    public int Id { get; set; }
    public string? Text { get; set; }
    public string? ImageUrl { get; set; }
}

public class WorksheetAttemptDto
{
    public int InstanceId { get; set; }
    public DateTime? CompletedDate { get; set; }
    public int DurationSeconds { get; set; }
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public int ScorePercent { get; set; }
}

public class SimilarWorksheetDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public bool IsPracticeTest { get; set; }
    public int? AverageScorePercent { get; set; }
}

public class WorksheetTeacherInsightsDto
{
    public List<HardestQuestionDto> HardestQuestions { get; set; } = new();
    public DifficultyDistributionDto DifficultyDistribution { get; set; } = new();
    public int ClassifiedCount { get; set; }
    public int TotalQuestionCount { get; set; }
    public int UnclassifiedCount { get; set; }
}

public class HardestQuestionDto
{
    public int QuestionId { get; set; }
    public int Order { get; set; }
    public string? Text { get; set; }
    public string? SubtopicName { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectPercent { get; set; }
}

public class DifficultyDistributionDto
{
    public int Easy { get; set; }
    public int Medium { get; set; }
    public int Hard { get; set; }
}

public class WorksheetCompletedResultDto
{
    public int InstanceId { get; set; }
    public int ScorePercent { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int EmptyCount { get; set; }
    public int DurationSeconds { get; set; }
    public List<WorksheetTopicSuccessDto> TopicSuccess { get; set; } = new();
    public WorksheetRankDto? Rank { get; set; }
}

public class WorksheetTopicSuccessDto
{
    public int? TopicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public int SuccessPercent { get; set; }
}

public class WorksheetRankDto
{
    public int Position { get; set; }
    public int TotalStudents { get; set; }
    public int ClassAveragePercent { get; set; }
}

public class WorksheetFromMistakesResultDto
{
    public int WorksheetId { get; set; }
}

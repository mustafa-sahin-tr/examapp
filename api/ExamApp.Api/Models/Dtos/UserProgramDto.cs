using System;
using System.Collections.Generic;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos
{
    public class UserProgramDto
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string ProgramName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsActive { get; set; }
        public string StudyType { get; set; } = string.Empty;
        public string StudyDuration { get; set; } = string.Empty;
        public int? QuestionsPerDay { get; set; }
        public int SubjectsPerDay { get; set; }
        public string RestDays { get; set; } = string.Empty;
        public string DifficultSubjects { get; set; } = string.Empty;
        public int CompletedPageCount { get; set; }
        public int TotalPageCount { get; set; }
        public int ProgressPercentage { get; set; }
        public List<UserProgramScheduleDto> Schedules { get; set; } = new List<UserProgramScheduleDto>();
        public List<UserProgramStudyPageScheduleDto> StudyItemSchedules { get; set; } = new List<UserProgramStudyPageScheduleDto>();
    }

    public class UserProgramScheduleDto
    {
        public int Id { get; set; }
        public int UserProgramId { get; set; }
        public DateTime ScheduleDate { get; set; }
        public int SubjectId { get; set; }
        public string SubjectName { get; set; } = string.Empty;
        public int? StudyDurationMinutes { get; set; }
        public int? QuestionCount { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedDate { get; set; }
        public string? Notes { get; set; }
    }

    public class UserProgramStudyPageScheduleDto
    {
        public int Id { get; set; }
        public int UserProgramId { get; set; }
        public int StudyItemId { get; set; }
        public string StudyItemTitle { get; set; } = string.Empty;
        public string? StudyItemCoverImageUrl { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime? CompletedDate { get; set; }

        // --- İçerik tipi (issue #141): öğrenci tarafı tipe göre render eder ---
        public StudyItemContentType ContentType { get; set; }

        // Link tipi — sadece ContentType == Link iken dolu
        public string? Url { get; set; }
        public StudyItemLinkPlatform? Platform { get; set; }

        // BookPageRange tipi — sadece ContentType == BookPageRange iken dolu
        public string? BookName { get; set; }
        public string? BookTestName { get; set; }
        public int? StartPage { get; set; }
        public int? EndPage { get; set; }
    }
}

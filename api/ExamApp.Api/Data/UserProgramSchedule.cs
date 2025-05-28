using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data
{
    public class UserProgramSchedule : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserProgramId { get; set; }

        public DateTime ScheduleDate { get; set; }

        public int SubjectId { get; set; }

        [MaxLength(100)]
        public string SubjectName { get; set; }

        public int? StudyDurationMinutes { get; set; }

        public int? QuestionCount { get; set; }

        public bool IsCompleted { get; set; } = false;

        public DateTime? CompletedDate { get; set; }

        public string? Notes { get; set; }

        // Navigation properties
        public virtual UserProgram UserProgram { get; set; }
    }
}

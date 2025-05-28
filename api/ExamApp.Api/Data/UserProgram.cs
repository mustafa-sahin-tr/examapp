using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data
{
    public class UserProgram : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } // Keycloak user ID

        [Required]
        [MaxLength(200)]
        public string ProgramName { get; set; }

        public string Description { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public bool IsActive { get; set; } = true;

        // Program configuration based on user selections
        public string StudyType { get; set; } = "time"; // "time" or "question"

        public string StudyDuration { get; set; } = "25-5"; // "25-5", "30-10", etc.

        public int? QuestionsPerDay { get; set; } // 8, 12, 16

        public int SubjectsPerDay { get; set; } = 1; // 1, 2, 3

        public string RestDays { get; set; } = ""; // Comma-separated day numbers: "1,6,7"

        public string DifficultSubjects { get; set; } = ""; // Comma-separated subject IDs: "1,3,4"

        // Navigation properties
        public virtual ICollection<UserProgramSchedule> Schedules { get; set; } = new List<UserProgramSchedule>();
    }
}

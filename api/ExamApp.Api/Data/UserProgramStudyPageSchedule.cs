using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data
{
    public class UserProgramStudyPageSchedule : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserProgramId { get; set; }

        [Required]
        public int StudyItemId { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        public bool IsCompleted { get; set; } = false;

        public DateTime? CompletedDate { get; set; }

        [ForeignKey("UserProgramId")]
        public virtual UserProgram UserProgram { get; set; }

        [ForeignKey("StudyItemId")]
        public virtual StudyItem StudyItem { get; set; }
    }
}

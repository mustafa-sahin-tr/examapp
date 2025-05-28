using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data
{
    public class ProgramStepAction : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Label { get; set; }
        [Required]
        public string Value { get; set; }

        // Foreign Key for ProgramStep
        public int ProgramStepId { get; set; }
        [ForeignKey("ProgramStepId")]
        public virtual ProgramStep ProgramStep { get; set; }
    }
}

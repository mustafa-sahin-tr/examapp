using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data
{
    public class ProgramStep
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Title { get; set; }

        public string Description { get; set; }

        public int Order { get; set; } // Bu alanı seed datada kullanacağız, TypeScript tarafında yoktu ama sıralama için önemli.

        public bool Multiple { get; set; }

        public virtual ICollection<ProgramStepOption> Options { get; set; } = new List<ProgramStepOption>();

        public virtual ICollection<ProgramStepAction> Actions { get; set; } = new List<ProgramStepAction>();
    }
}

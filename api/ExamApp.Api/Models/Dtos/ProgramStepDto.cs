using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos
{
    public class ProgramStepDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int Order { get; set; }
        public bool Multiple { get; set; }
        public List<ProgramStepOptionDto> Options { get; set; } = new List<ProgramStepOptionDto>();
        public List<ProgramStepActionDto> Actions { get; set; } = new List<ProgramStepActionDto>();
    }
}

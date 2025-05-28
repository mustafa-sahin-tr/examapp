using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos
{
    public class CreateProgramRequestDto
    {
        public string ProgramName { get; set; }
        public string Description { get; set; }
        public List<UserSelectionDto> UserSelections { get; set; } = new List<UserSelectionDto>();
    }

    public class UserSelectionDto
    {
        public int StepId { get; set; }
        public List<string> SelectedValues { get; set; } = new List<string>();
    }
}

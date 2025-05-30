using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos
{
    public class CreateProgramRequestDto
    {
        public string ProgramName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public List<UserSelectionDto> UserSelections { get; set; } = new List<UserSelectionDto>();
    }

    public class UserSelectionDto
    {
        public int StepId { get; set; }
        public List<string> SelectedValues { get; set; } = new List<string>();
    }
}

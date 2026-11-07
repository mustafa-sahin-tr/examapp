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

        /// <summary>
        /// issue #885: zorlandığı dersler, öğrencinin sınıf derslerinden (<c>GET api/program/subjects</c>) id ile. Doluysa sihirbazın
        /// eski 7. adımı (sabit dört ders) yok sayılır; null ise eski davranış.
        /// </summary>
        public List<int>? DifficultSubjectIds { get; set; }
    }

    /// <summary>issue #885: programa girecek ders (öğrencinin sınıfının dersleri; takvim bunlardan kurulur).</summary>
    public class ProgramSubjectDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class UserSelectionDto
    {
        public int StepId { get; set; }
        public List<string> SelectedValues { get; set; } = new List<string>();
    }
}

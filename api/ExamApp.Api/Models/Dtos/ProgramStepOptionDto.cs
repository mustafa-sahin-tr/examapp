namespace ExamApp.Api.Models.Dtos
{
    public class ProgramStepOptionDto
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string Value { get; set; }
        public bool Selected { get; set; }
        public string Icon { get; set; }
        public int? NextStep { get; set; }
    }
}

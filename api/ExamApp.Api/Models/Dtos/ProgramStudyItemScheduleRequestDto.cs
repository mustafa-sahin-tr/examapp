using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos
{
    public class ProgramStudyItemScheduleRequestDto
    {
        public List<ProgramStudyItemScheduleItemDto> Items { get; set; } = new List<ProgramStudyItemScheduleItemDto>();
    }

    public class ProgramStudyItemScheduleItemDto
    {
        public int StudyItemId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
